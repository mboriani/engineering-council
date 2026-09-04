using System.Globalization;
using System.Text;

namespace EngineeringCouncil.Infrastructure.Evaluation;

/// <summary>
/// Renders an <see cref="EvaluationReport"/> as <c>evaluation-report.md</c> — an
/// INTERNAL calibration artifact (Milestone 012). It is never consumed by the external
/// Engineering Review application, which continues to read only
/// <c>engineering-review-package.json</c>.
///
/// The report presents MEASUREMENTS. It deliberately never declares a "best" provider,
/// ranks providers, or assigns weights.
/// </summary>
public sealed class EvaluationReportExporter
{
    public string Export(EvaluationReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Evaluation Report");
        sb.AppendLine();
        sb.AppendLine($"> Generated {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}Z · internal calibration artifact");
        sb.AppendLine();
        sb.AppendLine("This report measures the existing platform. It introduces no analysis capability and");
        sb.AppendLine("does not influence `engineering-review-package.json`, which remains the only artifact the");
        sb.AppendLine("external Engineering Review application consumes. **Measurements only — no provider is");
        sb.AppendLine("declared better, ranked, or weighted.**");
        sb.AppendLine();

        Scope(sb, report);
        ExecutionSummary(sb, report);
        ProviderComparison(sb, report);
        ContextSelection(sb, report);
        PromptCalibration(sb, report);
        Reconciliation(sb, report);
        Quality(sb, report);
        Operational(sb, report);
        PackageValidation(sb, report);
        Observations(sb, report);

        return sb.ToString();
    }

    private static void Scope(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Scope");
        sb.AppendLine();
        sb.AppendLine($"- **Repositories evaluated:** {report.RepositoriesEvaluated.Count} "
            + $"({string.Join(", ", report.RepositoriesEvaluated)})");
        sb.AppendLine($"- **Providers evaluated:** {(report.ProvidersEvaluated.Count == 0 ? "—" : string.Join(", ", report.ProvidersEvaluated))}");
        sb.AppendLine($"- **Evaluation runs:** {report.Runs.Count}");
        sb.AppendLine();

        var purposes = report.Runs
            .GroupBy(r => r.Repository, StringComparer.Ordinal)
            .Select(g => (Repository: g.Key, g.First().RepositoryPurpose))
            .Where(x => !string.IsNullOrWhiteSpace(x.RepositoryPurpose))
            .ToList();

        if (purposes.Count > 0)
        {
            sb.AppendLine("| Repository | Why it is in the dataset |");
            sb.AppendLine("|------------|--------------------------|");
            foreach (var (repository, purpose) in purposes)
                sb.AppendLine($"| `{repository}` | {purpose} |");
            sb.AppendLine();
        }

        if (report.ProvidersUnavailable.Count > 0)
        {
            sb.AppendLine("**Provider executions that could not run** (reported, not hidden):");
            sb.AppendLine();
            foreach (var reason in report.ProvidersUnavailable) sb.AppendLine($"- {reason}");
            sb.AppendLine();
        }
    }

    private static void ExecutionSummary(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Execution summary");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | Status | Files | Obs | Raw | Consolidated | Total ms |");
        sb.AppendLine("|------------|-----------|--------|------:|----:|----:|-------------:|---------:|");
        foreach (var run in report.Runs)
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {run.Status} "
                + $"| {run.RepositoryFiles} | {run.Quality.Observations} | {run.Reconciliation.RawFindings} "
                + $"| {run.Reconciliation.ConsolidatedFindings} | {N(run.Operational.TotalMs)} |");
        sb.AppendLine();
    }

    private static void ProviderComparison(StringBuilder sb, EvaluationReport report)
    {
        // One row per provider selection, averaged across repositories — identical inputs.
        var groups = report.Runs
            .Where(r => r.Status == "Completed")
            .GroupBy(r => string.Join(", ", r.Providers), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine("## Provider comparison");
        sb.AppendLine();
        if (groups.Count == 0)
        {
            sb.AppendLine("_No completed runs to compare._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Averages across the same repositories (identical inputs per repository).");
        sb.AppendLine();
        sb.AppendLine("| Provider selection | Runs | Avg exec ms | Avg observations | Avg raw | Avg consolidated | Dup. reduction % | Avg confidence(High) | Tokens |");
        sb.AppendLine("|--------------------|-----:|------------:|-----------------:|--------:|-----------------:|-----------------:|---------------------:|-------:|");
        foreach (var group in groups)
        {
            var runs = group.ToList();
            var highConfidence = runs.Sum(r => r.Quality.FindingsByConfidence.GetValueOrDefault("High"));
            var tokens = runs.Sum(r => r.Usage.TotalTokens ?? 0);
            sb.AppendLine($"| {group.Key} | {runs.Count} | {N(runs.Average(r => r.Operational.TotalMs))} "
                + $"| {N(runs.Average(r => (double)r.Quality.Observations))} "
                + $"| {N(runs.Average(r => (double)r.Reconciliation.RawFindings))} "
                + $"| {N(runs.Average(r => (double)r.Reconciliation.ConsolidatedFindings))} "
                + $"| {N(runs.Average(r => r.Reconciliation.DuplicateReductionPercent))} "
                + $"| {highConfidence} | {(tokens == 0 ? "—" : tokens.ToString())} |");
        }
        sb.AppendLine();
        sb.AppendLine("> Duplicate rate is expressed as the reconciliation duplicate-reduction percentage.");
        sb.AppendLine("> No ranking is implied: these are measurements of different sources on the same inputs.");
        sb.AppendLine();
    }

    private static void ContextSelection(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Context selection");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | Repo files | Avg selected | Max selected | Avg chars | Avg omitted | Repeated sends | Truncations |");
        sb.AppendLine("|------------|-----------|-----------:|-------------:|-------------:|----------:|------------:|---------------:|------------:|");
        foreach (var run in report.Runs)
        {
            var c = run.Context;
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {c.RepositoryFiles} "
                + $"| {N(c.SelectedFilesAverage)} | {c.SelectedFilesMax} | {N(c.SelectedCharactersAverage)} "
                + $"| {N(c.OmittedFilesAverage)} | {c.RepeatedFileSelections} | {c.ResponseTruncations} |");
        }
        sb.AppendLine();
        sb.AppendLine("_Signals only — the context selector is deliberately unchanged in this milestone._");
        sb.AppendLine();
    }

    private static void PromptCalibration(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Prompt calibration");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | LLM steps | Schema failures | Invalid % | Repairs | Repair success | Hallucinated refs | No location | Unsupported types |");
        sb.AppendLine("|------------|-----------|----------:|----------------:|----------:|--------:|---------------:|------------------:|------------:|------------------:|");
        foreach (var run in report.Runs)
        {
            var p = run.PromptCalibration;
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {p.LlmExecutions} "
                + $"| {p.SchemaValidationFailures} | {N(p.InvalidResponseRate * 100)} | {p.RepairAttempts} "
                + $"| {N(p.RepairSuccessRate * 100)} | {p.HallucinatedFileReferences} "
                + $"| {p.ObservationsMissingLocation} | {p.UnsupportedObservationTypes} |");
        }
        sb.AppendLine();
        sb.AppendLine("_Prompts are NOT redesigned in this milestone; evidence is collected first._");
        sb.AppendLine();
    }

    private static void Reconciliation(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Reconciliation calibration");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | Raw | Consolidated | Reduction % | Multi-provider | Single-provider | Contradictions | False-merge candidates |");
        sb.AppendLine("|------------|-----------|----:|-------------:|------------:|---------------:|----------------:|---------------:|-----------------------:|");
        foreach (var run in report.Runs)
        {
            var r = run.Reconciliation;
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {r.RawFindings} | {r.ConsolidatedFindings} "
                + $"| {N(r.DuplicateReductionPercent)} | {r.MultiProviderFindings} | {r.SingleProviderFindings} "
                + $"| {r.Contradictions} | {r.FalseMergeCandidates} |");
        }
        sb.AppendLine();

        var distributions = report.Runs.SelectMany(r => r.Reconciliation.AgreementDistribution)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (distributions.Count > 0)
        {
            sb.AppendLine("**Agreement distribution** (findings by number of independent providers)");
            sb.AppendLine();
            foreach (var group in distributions)
                sb.AppendLine($"- **{group.Key} provider(s):** {group.Sum(kv => kv.Value)}");
            sb.AppendLine();
        }
        sb.AppendLine("_False-merge candidates are consolidated findings whose sources share no file; they are a");
        sb.AppendLine("manual review queue, not defects. No reconciliation rule is changed in this milestone._");
        sb.AppendLine();
    }

    private static void Quality(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Quality metrics (informational)");
        sb.AppendLine();
        Distribution(sb, "Observations by discipline", report.Runs.SelectMany(r => r.Quality.ObservationsByDiscipline));
        Distribution(sb, "Findings by discipline", report.Runs.SelectMany(r => r.Quality.FindingsByDiscipline));
        Distribution(sb, "Findings by provider", report.Runs.SelectMany(r => r.Quality.FindingsByProvider));
        Distribution(sb, "Findings by severity", report.Runs.SelectMany(r => r.Quality.FindingsBySeverity));
        Distribution(sb, "Findings by confidence", report.Runs.SelectMany(r => r.Quality.FindingsByConfidence));

        var withFindings = report.Runs.Where(r => r.Quality.Observations > 0).ToList();
        if (withFindings.Count > 0)
            sb.AppendLine($"- **Average provider agreement:** {N(withFindings.Average(r => r.Quality.ProviderAgreementPercent))}% of consolidated findings backed by ≥2 providers");
        sb.AppendLine();
        sb.AppendLine("_These metrics are informational and never affect package generation._");
        sb.AppendLine();
    }

    private static void Operational(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Operational metrics");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | Scan | Acquisition | Interpret | Analyze | Reconcile | Package | Persist | Slowest |");
        sb.AppendLine("|------------|-----------|-----:|------------:|----------:|--------:|----------:|--------:|--------:|---------|");
        foreach (var run in report.Runs)
        {
            var o = run.Operational;
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {N(o.ScanMs)} | {N(o.AcquisitionMs)} "
                + $"| {N(o.InterpretationMs)} | {N(o.AnalysisMs)} | {N(o.ReconciliationMs)} | {N(o.PackageBuildMs)} "
                + $"| {N(o.PersistenceMs)} | {o.SlowestStage} |");
        }
        sb.AppendLine();
        sb.AppendLine("Times in milliseconds. Provider latency (avg / max):");
        sb.AppendLine();
        foreach (var run in report.Runs.Where(r => r.Operational.ProviderLatencyAverageMs > 0))
            sb.AppendLine($"- `{run.Repository}` [{string.Join(", ", run.Providers)}]: "
                + $"{N(run.Operational.ProviderLatencyAverageMs)} ms avg · {N(run.Operational.ProviderLatencyMaxMs)} ms max");
        sb.AppendLine();

        var usage = report.Runs.Where(r => r.Usage.TotalTokens is > 0).ToList();
        sb.AppendLine("**Token usage and cost**");
        sb.AppendLine();
        if (usage.Count == 0)
        {
            var reason = report.Runs.Select(r => r.Usage.CostUnavailableReason).FirstOrDefault(r => r is not null);
            sb.AppendLine($"_No provider reported token usage in these runs ({reason ?? "no usage reported"})._");
        }
        else
        {
            foreach (var run in usage)
                sb.AppendLine($"- `{run.Repository}` [{string.Join(", ", run.Providers)}]: "
                    + $"{run.Usage.InputTokens ?? 0} in / {run.Usage.OutputTokens ?? 0} out / {run.Usage.TotalTokens} total"
                    + (run.Usage.EstimatedCost is { } cost
                        ? $" · ~{cost.ToString("0.0000", CultureInfo.InvariantCulture)} {run.Usage.Currency} (estimated)"
                        : $" · cost unavailable ({run.Usage.CostUnavailableReason})"));
        }
        sb.AppendLine();
    }

    private static void PackageValidation(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Package validation");
        sb.AppendLine();
        sb.AppendLine("| Repository | Providers | schemaVersion | Size (bytes) | Serialize ms | Deterministic | Root props | No type leaks |");
        sb.AppendLine("|------------|-----------|---------------|-------------:|-------------:|---------------|------------|---------------|");
        foreach (var run in report.Runs)
        {
            var p = run.Package;
            sb.AppendLine($"| `{run.Repository}` | {string.Join(", ", run.Providers)} | {(string.IsNullOrEmpty(p.SchemaVersion) ? "—" : p.SchemaVersion)} "
                + $"| {p.SizeBytes} | {N(p.SerializationMs)} | {Yes(p.DeterministicSerialization)} "
                + $"| {(p.RequiredRootPropertiesPresent ? "complete" : "MISSING: " + string.Join(", ", p.MissingRootProperties))} "
                + $"| {Yes(p.ContainsNoRuntimeTypeLeaks)} |");
        }
        sb.AppendLine();
        sb.AppendLine("_The consumer-DTO gate remains the authoritative compatibility test (`PackageContractTests`)._");
        sb.AppendLine();
    }

    private static void Observations(StringBuilder sb, EvaluationReport report)
    {
        sb.AppendLine("## Observations and recommendations for future milestones");
        sb.AppendLine();

        var completed = report.Runs.Where(r => r.Status == "Completed").ToList();
        if (completed.Count == 0)
        {
            sb.AppendLine("- No completed runs: nothing can be concluded from this evaluation.");
            sb.AppendLine();
            return;
        }

        var notes = new List<string>();

        var slowest = completed.GroupBy(r => r.Operational.SlowestStage, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).First();
        notes.Add($"The slowest stage in {slowest.Count()}/{completed.Count} runs is **{slowest.Key}**.");

        var repeated = completed.Where(r => r.Context.RepeatedFileSelections > 0).ToList();
        if (repeated.Count > 0)
            notes.Add($"{repeated.Count} run(s) re-send files across discipline contexts "
                + $"(max {repeated.Max(r => r.Context.RepeatedFileSelections)} redundant selections) — a selector-tuning candidate.");

        var hallucinations = completed.Sum(r => r.PromptCalibration.HallucinatedFileReferences);
        notes.Add(hallucinations == 0
            ? "No hallucinated file references were dropped by the guard in these runs."
            : $"{hallucinations} claimed file reference(s) did not exist and were dropped — prompt-calibration candidate.");

        var invalid = completed.Sum(r => r.PromptCalibration.SchemaValidationFailures);
        var llmSteps = completed.Sum(r => r.PromptCalibration.LlmExecutions);
        notes.Add(llmSteps == 0
            ? "No LLM steps executed, so structured-output quality could not be measured (configure a real provider)."
            : $"{invalid}/{llmSteps} LLM step(s) failed structured-output validation.");

        var reduction = completed.Average(r => r.Reconciliation.DuplicateReductionPercent);
        notes.Add($"Average reconciliation duplicate reduction is {N(reduction)}%; "
            + $"{completed.Sum(r => r.Reconciliation.FalseMergeCandidates)} false-merge candidate(s) await manual review.");

        var missingLocation = completed.Sum(r => r.PromptCalibration.ObservationsMissingLocation);
        if (missingLocation > 0)
            notes.Add($"{missingLocation} observation(s) carried no file reference — location grounding is a calibration candidate.");

        if (report.Runs.Any(r => !r.Package.RequiredRootPropertiesPresent || !r.Package.DeterministicSerialization))
            notes.Add("**A package failed structural validation — investigate before any contract change.**");
        else
            notes.Add("Every produced package kept the required root properties, deterministic serialization, and no type leaks.");

        foreach (var note in notes) sb.AppendLine($"- {note}");
        sb.AppendLine();
        sb.AppendLine("These are inputs for future milestones. No selector, prompt, or reconciliation rule was");
        sb.AppendLine("changed here, and no provider ranking or weighting is implied.");
        sb.AppendLine();
    }

    private static void Distribution(StringBuilder sb, string heading, IEnumerable<KeyValuePair<string, int>> values)
    {
        var totals = values.GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Total: g.Sum(kv => kv.Value)))
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        if (totals.Count == 0) return;

        sb.AppendLine($"**{heading}**");
        sb.AppendLine();
        foreach (var (key, total) in totals) sb.AppendLine($"- **{key}:** {total}");
        sb.AppendLine();
    }

    private static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Yes(bool value) => value ? "yes" : "**no**";
}
