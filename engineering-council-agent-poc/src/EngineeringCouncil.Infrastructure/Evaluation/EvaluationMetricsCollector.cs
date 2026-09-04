using System.Diagnostics;
using System.Text.Json;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;

namespace EngineeringCouncil.Infrastructure.Evaluation;

/// <summary>
/// Optional pricing for cost estimation, keyed by "provider/model". Pricing is
/// CONFIGURATION, never domain logic: when it is absent the evaluation reports token
/// usage and states that cost is unavailable rather than inventing a number.
/// </summary>
public sealed record ModelPricing(decimal InputPerMillion, decimal OutputPerMillion, string Currency = "USD");

/// <summary>
/// Turns a completed <see cref="AnalysisResult"/> into evaluation metrics
/// (Milestone 012). Pure measurement: it reads the run and the produced package and
/// never mutates either, so collecting metrics cannot influence what is generated.
/// </summary>
public static class EvaluationMetricsCollector
{
    private static readonly HashSet<string> KnownObservationTypes = new(
    [
        ObservationTypes.CircularDependency, ObservationTypes.LayerViolation, ObservationTypes.HighComplexity,
        ObservationTypes.MissingTimeout, ObservationTypes.MissingRetryPolicy, ObservationTypes.BroadExceptionHandling,
        ObservationTypes.HardcodedSecret, ObservationTypes.MissingAuthorization, ObservationTypes.MissingTests,
        ObservationTypes.MissingHealthCheck, ObservationTypes.MissingDocumentation, ObservationTypes.CodeHotspot,
        ObservationTypes.GeneralObservation, ObservationTypes.Unknown
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>Root properties the external consumer contract relies on.</summary>
    private static readonly string[] RequiredRootProperties =
        ["schemaVersion", "repository", "generatedAt", "analysisRunId", "executiveSummary",
         "overallEngineeringHealth", "overallRisk", "findings", "metrics"];

    public static EvaluationRun Collect(
        EvaluationRepository repository,
        IReadOnlyList<string> providers,
        AnalysisResult result,
        IReadOnlyDictionary<string, ModelPricing>? pricing = null)
    {
        var run = result.Run;
        var records = run.ProviderExecution?.Records ?? [];

        return new EvaluationRun
        {
            Repository = repository.Name,
            RepositoryPurpose = repository.Purpose,
            Providers = providers,
            Disciplines = run.RequestedDisciplines.Select(d => d.ToString()).ToList(),
            RunId = run.RunId,
            Status = run.Status.ToString(),
            Error = run.Error?.Split('\n')[0],
            RepositoryFiles = run.FilesScanned,
            Projects = run.Projects,
            Operational = CollectOperational(run, records),
            Context = CollectContext(run, records),
            PromptCalibration = CollectPromptCalibration(run, records),
            Reconciliation = CollectReconciliation(run),
            Quality = CollectQuality(run),
            Usage = CollectUsage(run, records, pricing),
            Package = ValidatePackage(result.Package),
            OutputDirectory = result.OutputDirectory
        };
    }

    // ── Operational ───────────────────────────────────────────────────────────

    private static OperationalMetrics CollectOperational(AnalysisRun run, IReadOnlyList<ProviderExecutionRecord> records)
    {
        var timings = run.StageTimings ?? new RunStageTimings();
        var latencies = records.Select(r => r.Duration.TotalMilliseconds).ToList();

        return new OperationalMetrics
        {
            TotalMs = Round(timings.Total),
            ScanMs = Round(timings.Scan),
            AcquisitionMs = Round(timings.Acquisition),
            InterpretationMs = Round(timings.Interpretation),
            AnalysisMs = Round(timings.Analysis),
            ReconciliationMs = Round(timings.Reconciliation),
            PackageBuildMs = Round(timings.PackageBuild),
            PersistenceMs = Round(timings.Persistence),
            SlowestStage = timings.SlowestStage,
            ProviderLatencyAverageMs = latencies.Count == 0 ? 0 : Math.Round(latencies.Average(), 1),
            ProviderLatencyMaxMs = latencies.Count == 0 ? 0 : Math.Round(latencies.Max(), 1)
        };
    }

    // ── Context selection ─────────────────────────────────────────────────────

    private static ContextMetrics CollectContext(AnalysisRun run, IReadOnlyList<ProviderExecutionRecord> records)
    {
        var contextSteps = records.Where(r => r.ContextFileCount > 0).ToList();
        var promptSizes = run.Evidence
            .Select(e => e.Metadata.TryGetValue("contextCharacterCount", out var c) && int.TryParse(c, out var n) ? n : 0)
            .Where(n => n > 0).ToList();

        // Redundant sends: everything selected across all steps beyond the repository's
        // own file count means discipline contexts overlap (measure only, no change).
        var totalSelected = contextSteps.Sum(r => r.ContextFileCount);

        return new ContextMetrics
        {
            RepositoryFiles = run.FilesScanned,
            SelectedFilesAverage = contextSteps.Count == 0 ? 0 : Math.Round(contextSteps.Average(r => r.ContextFileCount), 1),
            SelectedFilesMax = contextSteps.Count == 0 ? 0 : contextSteps.Max(r => r.ContextFileCount),
            SelectedCharactersAverage = contextSteps.Count == 0 ? 0 : Math.Round(contextSteps.Average(r => r.ContextCharacterCount), 1),
            SelectedCharactersMax = contextSteps.Count == 0 ? 0 : contextSteps.Max(r => r.ContextCharacterCount),
            OmittedFilesAverage = contextSteps.Count == 0 ? 0
                : Math.Round(contextSteps.Average(r => Math.Max(0, run.FilesScanned - r.ContextFileCount)), 1),
            PromptCharactersAverage = promptSizes.Count == 0 ? 0 : Math.Round(promptSizes.Average(), 1),
            ResponseTruncations = run.Evidence.Count(e =>
                e.Metadata.TryGetValue("truncated", out var t) && string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)),
            RepeatedFileSelections = Math.Max(0, totalSelected - run.FilesScanned)
        };
    }

    // ── Prompt calibration ────────────────────────────────────────────────────

    private static PromptCalibrationMetrics CollectPromptCalibration(
        AnalysisRun run, IReadOnlyList<ProviderExecutionRecord> records)
    {
        var llmRecords = records.Where(r => r.ProviderType == EvidenceProviderType.LLM).ToList();
        var interpretation = run.ObservationInterpretationSummary;

        var repairAttempts = run.Evidence.Count(e =>
            e.Metadata.TryGetValue("repairAttempted", out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        return new PromptCalibrationMetrics
        {
            LlmExecutions = llmRecords.Count,
            // A schema-validation failure surfaces as a failed LLM step mentioning validation.
            SchemaValidationFailures = llmRecords.Count(r => !r.Success
                && (r.ErrorMessage?.Contains("validation", StringComparison.OrdinalIgnoreCase) == true
                    || r.ErrorMessage?.Contains("JSON", StringComparison.OrdinalIgnoreCase) == true)),
            RepairAttempts = repairAttempts,
            RepairSuccesses = repairAttempts,   // evidence exists only when the repair produced a valid payload
            HallucinatedFileReferences = interpretation?.InvalidFileReferencesDropped ?? 0,
            ObservationsMissingLocation = run.Observations.Count(o => o.FileReferences.Count == 0),
            UnsupportedObservationTypes = run.Observations.Count(o => !KnownObservationTypes.Contains(o.ObservationType)),
            DisciplineMismatches = interpretation?.DisciplineMismatches ?? 0,
            UnsupportedEvidence = interpretation?.EvidenceUnsupported ?? 0,
            InterpreterFailures = interpretation?.InterpreterFailures ?? 0
        };
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    private static ReconciliationMetrics CollectReconciliation(AnalysisRun run)
    {
        var summary = run.ReconciliationSummary;

        return new ReconciliationMetrics
        {
            RawFindings = summary?.RawFindingCount ?? run.RawFindings.Count,
            ConsolidatedFindings = summary?.ConsolidatedFindingCount ?? run.Findings.Count,
            MultiProviderFindings = summary?.MultiProviderFindingCount ?? 0,
            SingleProviderFindings = summary?.SingleProviderFindingCount ?? 0,
            Contradictions = summary?.ContradictionCount ?? 0,
            AgreementDistribution = run.Findings
                .GroupBy(f => f.AgreementCount.ToString())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count()),
            // Consolidated findings whose sources share no file are the manual-review queue.
            FalseMergeCandidates = run.Findings.Count(f => f.IsConsolidated
                && f.ContradictionReasons.Any(r => r.Contains("different files", StringComparison.OrdinalIgnoreCase)))
        };
    }

    // ── Quality (informational only) ──────────────────────────────────────────

    private static QualityMetrics CollectQuality(AnalysisRun run)
    {
        var findings = run.Findings;
        var multiProvider = findings.Count(f => f.AgreementCount >= 2);

        return new QualityMetrics
        {
            Observations = run.Observations.Count,
            ObservationsByDiscipline = Count(run.Observations.Select(o => o.Discipline.ToString())),
            ObservationsByProvider = Count(run.Observations.Select(o => o.SourceProvider)),
            FindingsByDiscipline = Count(findings.Select(f => f.Category.ToString())),
            FindingsByProvider = Count(findings.SelectMany(f => f.SupportingProviders)),
            FindingsBySeverity = Count(findings.Select(f => f.Severity.ToString())),
            FindingsByConfidence = Count(findings.Select(f => f.Confidence.ToString())),
            ProviderAgreementPercent = findings.Count == 0 ? 0 : Math.Round(100.0 * multiProvider / findings.Count, 1)
        };
    }

    // ── Provider usage + optional cost ────────────────────────────────────────

    private static ProviderUsageMetrics CollectUsage(
        AnalysisRun run, IReadOnlyList<ProviderExecutionRecord> records,
        IReadOnlyDictionary<string, ModelPricing>? pricing)
    {
        var input = SumMetadata(run, "inputTokens");
        var output = SumMetadata(run, "outputTokens");
        var models = run.Evidence
            .Where(e => !string.IsNullOrWhiteSpace(e.ProviderVersion))
            .GroupBy(e => e.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ProviderVersion, StringComparer.OrdinalIgnoreCase);

        decimal? cost = null;
        string? unavailable = null;
        if (pricing is null || pricing.Count == 0) unavailable = "no pricing configured";
        else if (input is null && output is null) unavailable = "provider reported no token usage";
        else
        {
            cost = 0m;
            foreach (var evidence in run.Evidence)
            {
                var model = evidence.ProviderVersion;
                if (!pricing.TryGetValue($"{evidence.ProviderName}/{model}", out var price)
                    && !pricing.TryGetValue(model, out price)) continue;

                var inTokens = MetadataInt(evidence.Metadata, "inputTokens");
                var outTokens = MetadataInt(evidence.Metadata, "outputTokens");
                cost += (inTokens / 1_000_000m) * price.InputPerMillion + (outTokens / 1_000_000m) * price.OutputPerMillion;
            }
            if (cost == 0m) { cost = null; unavailable = "no pricing matched the models used"; }
            else cost = Math.Round(cost.Value, 4);
        }

        return new ProviderUsageMetrics
        {
            Executions = records.Count,
            Failures = records.Count(r => !r.Success),
            EvidenceCount = run.Evidence.Count,
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input is null && output is null ? null : (input ?? 0) + (output ?? 0),
            EstimatedCost = cost,
            CostUnavailableReason = unavailable,
            ModelsUsed = models,
            FailureReasons = records.Where(r => !r.Success && r.ErrorMessage is not null)
                .Select(r => $"{r.ProviderName}{(r.RequestedDiscipline is { } d ? $"/{d}" : "")}: {r.ErrorMessage}")
                .Distinct(StringComparer.Ordinal).ToList()
        };
    }

    // ── Package validation ────────────────────────────────────────────────────

    private static PackageValidationMetrics ValidatePackage(EngineeringReviewPackage package)
    {
        var stopwatch = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);
        stopwatch.Stop();
        var second = JsonSerializer.Serialize(package, CouncilJson.Options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var missing = RequiredRootProperties.Where(p => !root.TryGetProperty(p, out _)).ToList();

        return new PackageValidationMetrics
        {
            SchemaVersion = package.SchemaVersion,
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(json),
            SerializationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            DeterministicSerialization = string.Equals(json, second, StringComparison.Ordinal),
            RequiredRootPropertiesPresent = missing.Count == 0,
            MissingRootProperties = missing,
            ContainsNoRuntimeTypeLeaks = !new[] { "$type", "System.", "PublicKeyToken", "EngineeringCouncil.Core" }
                .Any(leak => json.Contains(leak, StringComparison.Ordinal))
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, int> Count(IEnumerable<string> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

    private static int? SumMetadata(AnalysisRun run, string key)
    {
        var values = run.Evidence
            .Select(e => e.Metadata.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : (int?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Sum();
    }

    private static int MetadataInt(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : 0;

    private static double Round(TimeSpan value) => Math.Round(value.TotalMilliseconds, 1);
}
