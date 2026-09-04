using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Summarizing;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 011.2 — domain &amp; metrics correctness regression tests:
///   A4 provider metrics are mutually exclusive (Successful / Partial / Failed),
///   A5 an unrecognized discipline maps to Unknown (never CodeQuality) and unclaimed
///     observations are surfaced without inflating CodeQuality,
///   A6 a valid-enum discipline with no registered analyzer fails fast before scan.
/// </summary>
public sealed class DomainMetricsCorrectnessTests
{
    private static readonly FindingCategory[] Registered =
    [
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    ];

    // ── A4 ─────────────────────────────────────────────────────────────────────

    private static EngineeringMetrics Metrics(params ProviderExecutionRecord[] records)
    {
        var run = new AnalysisRun
        {
            RunId = "20260807-000000-abc123",
            TargetPath = "/repo",
            SolutionName = "Repo",
            ProviderExecution = ProviderExecutionReport.FromRecords(records),
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            Status = AnalysisRunStatus.Completed
        };
        return EngineeringMetrics.From(run);
    }

    [Fact]
    public void All_successful_executions_mark_provider_successful()
    {
        var m = Metrics(
            new ProviderExecutionRecord { ProviderName = "Mock", Success = true, EvidenceCount = 1 },
            new ProviderExecutionRecord { ProviderName = "Mock", Success = true, EvidenceCount = 1 });

        Assert.Equal(1, m.SuccessfulProviders);
        Assert.Equal(0, m.PartialProviders);
        Assert.Equal(0, m.FailedProviders);
    }

    [Fact]
    public void All_failed_executions_mark_provider_failed()
    {
        var m = Metrics(
            new ProviderExecutionRecord { ProviderName = "Mock", Success = false, EvidenceCount = 0 },
            new ProviderExecutionRecord { ProviderName = "Mock", Success = false, EvidenceCount = 0 });

        Assert.Equal(0, m.SuccessfulProviders);
        Assert.Equal(0, m.PartialProviders);
        Assert.Equal(1, m.FailedProviders);
    }

    [Fact]
    public void Mixed_success_and_failure_mark_provider_partial_not_successful()
    {
        var m = Metrics(
            new ProviderExecutionRecord { ProviderName = "Mock", Success = true, EvidenceCount = 1 },
            new ProviderExecutionRecord { ProviderName = "Mock", Success = false, EvidenceCount = 0 });

        Assert.Equal(0, m.SuccessfulProviders);
        Assert.Equal(1, m.PartialProviders);
        Assert.Equal(0, m.FailedProviders);
    }

    [Fact]
    public void Zero_executions_are_never_successful_failed_or_partial()
    {
        var m = Metrics(); // no records → no providers

        Assert.Equal(0, m.SuccessfulProviders);
        Assert.Equal(0, m.PartialProviders);
        Assert.Equal(0, m.FailedProviders);
    }

    [Fact]
    public void Provider_states_are_mutually_exclusive_across_multiple_providers()
    {
        var m = Metrics(
            new ProviderExecutionRecord { ProviderName = "Alpha", Success = true, EvidenceCount = 1 },
            new ProviderExecutionRecord { ProviderName = "Beta", Success = true, EvidenceCount = 1 },
            new ProviderExecutionRecord { ProviderName = "Beta", Success = false, EvidenceCount = 0 },
            new ProviderExecutionRecord { ProviderName = "Gamma", Success = false, EvidenceCount = 0 });

        Assert.Equal(1, m.SuccessfulProviders);   // Alpha: all succeeded
        Assert.Equal(1, m.PartialProviders);       // Beta: mixed
        Assert.Equal(1, m.FailedProviders);        // Gamma: all failed
        Assert.Equal(3, m.SuccessfulProviders + m.PartialProviders + m.FailedProviders);
        Assert.Equal(2, m.EvidenceSources);        // Alpha + Beta produced evidence
    }

    // ── A5 ─────────────────────────────────────────────────────────────────────

    private static AnalyzerContext ContextWith(params string[] files) => new()
    {
        Snapshot = new RepositorySnapshot
        {
            RootPath = "/repo",
            SolutionName = "Repo",
            Files = files.Select(p => new ScannedFile
            {
                RelativePath = p, Extension = Path.GetExtension(p), SizeBytes = 1, LineCount = 1
            }).ToList()
        }
    };

    private static Evidence LlmEvidence(string raw) => new()
    {
        ProviderName = "Mock", ProviderType = EvidenceProviderType.LLM, RawResponse = raw, Success = true
    };

    [Fact]
    public async Task Unrecognized_discipline_maps_to_unknown_not_code_quality()
    {
        var raw = """
        { "observations": [
          { "title": "weird", "discipline": "NotARealDiscipline", "severity": "High" } ] }
        """;

        var obs = await new StructuredLlmEvidenceInterpreter().InterpretAsync(LlmEvidence(raw), ContextWith());

        var single = Assert.Single(obs);
        Assert.Equal(FindingCategory.Unknown, single.Discipline);
        Assert.NotEqual(FindingCategory.CodeQuality, single.Discipline);
    }

    [Fact]
    public async Task Recognized_but_unanalyzed_discipline_is_never_rewritten()
    {
        var raw = """
        { "observations": [
          { "title": "perf", "discipline": "Performance", "severity": "High" } ] }
        """;

        var obs = await new StructuredLlmEvidenceInterpreter().InterpretAsync(LlmEvidence(raw), ContextWith());

        var single = Assert.Single(obs);
        Assert.Equal(FindingCategory.Performance, single.Discipline);
        Assert.NotEqual(FindingCategory.CodeQuality, single.Discipline);
    }

    [Fact]
    public async Task Code_quality_analyzer_does_not_consume_unknown_observations()
    {
        var analyzer = new CodeQualityAnalyzer();
        var observations = new List<EngineeringObservation>
        {
            new() { Id = "OBS-001", Discipline = FindingCategory.CodeQuality,
                ObservationType = ObservationTypes.HighComplexity, Title = "Complex", SourceProvider = "Mock" },
            new() { Id = "OBS-002", Discipline = FindingCategory.Unknown,
                ObservationType = ObservationTypes.Unknown, Title = "Noise", SourceProvider = "Mock" }
        };

        var findings = await analyzer.AnalyzeAsync(observations, ContextWith());

        var finding = Assert.Single(findings);
        Assert.Equal(FindingCategory.CodeQuality, finding.Category);
        Assert.Equal(["OBS-001"], finding.ObservationIds);
        Assert.Equal(1, finding.SupportingObservationCount);
    }

    [Fact]
    public void Metrics_count_unclaimed_observations_without_inflating_code_quality()
    {
        var run = new AnalysisRun
        {
            RunId = "20260807-000000-abc123",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Observations =
            [
                new EngineeringObservation { Id = "OBS-001", Discipline = FindingCategory.CodeQuality },
                new EngineeringObservation { Id = "OBS-002", Discipline = FindingCategory.Unknown },
                new EngineeringObservation { Id = "OBS-003", Discipline = FindingCategory.Unknown }
            ]
        };

        var m = EngineeringMetrics.From(run);

        Assert.Equal(3, m.TotalObservations);
        Assert.Equal(2, m.UnclaimedObservations);
    }

    [Fact]
    public async Task Interpretation_summary_reports_unclaimed_and_unknown_discipline()
    {
        var raw = """
        { "observations": [
          { "title": "a", "discipline": "CodeQuality" },
          { "title": "b", "discipline": "NotADiscipline" } ] }
        """;
        var pipeline = new EvidenceInterpretationPipeline(
            new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()]));

        var result = await pipeline.InterpretAsync([LlmEvidence(raw)], ContextWith());

        Assert.Equal(2, result.Summary.ObservationsProduced);
        Assert.Equal(1, result.Summary.UnclaimedObservationCount);
        Assert.Equal(1, result.Summary.ObservationsByDiscipline["Unknown"]);
        Assert.Equal(1, EvidenceSummary.From(null, result.Summary).UnclaimedObservations);
    }

    // ── A6 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_reports_every_unregistered_discipline()
    {
        var unsupported = AnalyzerDisciplineValidator.Unsupported(
            [FindingCategory.Performance, FindingCategory.Security, FindingCategory.Maintainability, FindingCategory.Unknown],
            Registered);

        Assert.Equal([FindingCategory.Performance, FindingCategory.Maintainability, FindingCategory.Unknown], unsupported);
    }

    [Fact]
    public void Validator_accepts_supported_and_empty_request()
    {
        Assert.Empty(AnalyzerDisciplineValidator.Unsupported([FindingCategory.Security, FindingCategory.Architecture], Registered));
        Assert.Empty(AnalyzerDisciplineValidator.Unsupported([], Registered));
        Assert.Empty(AnalyzerDisciplineValidator.Unsupported(null, Registered));
    }

    [Fact]
    public void Ensure_supported_throws_listing_all_unsupported_disciplines()
    {
        var ex = Assert.Throws<UnsupportedDisciplineException>(() =>
            AnalyzerDisciplineValidator.EnsureSupported([FindingCategory.Performance, FindingCategory.Dependencies], Registered));

        Assert.Equal([FindingCategory.Performance, FindingCategory.Dependencies], ex.Unsupported);
        Assert.Contains("Performance", ex.Message);
        Assert.Contains("Dependencies", ex.Message);
        Assert.Contains("no registered analyzer", ex.Message);
    }

    private sealed class ThrowingScanner : IRepositoryScanner
    {
        public bool Invoked { get; private set; }

        public Task<RepositorySnapshot> ScanAsync(
            string rootPath, ScanOptions options, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            throw new InvalidOperationException("Scanner must not run when disciplines are unsupported.");
        }
    }

    private sealed class ThrowingRepository : IAnalysisRunRepository
    {
        public bool Invoked { get; private set; }

        public Task<string> SaveAsync(AnalysisRun run, EngineeringReviewPackage package, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            throw new InvalidOperationException("Nothing must be persisted when disciplines are unsupported.");
        }

        public Task<AnalysisRun?> GetAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult<AnalysisRun?>(null);

        public Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static AnalysisPipeline PipelineWith(
        IRepositoryScanner scanner, IAnalysisRunRepository repository,
        IReadOnlyList<FindingCategory>? configuredDisciplines, params IAnalyzerAgent[] analyzers)
    {
        var options = new EvidenceOptions { Providers = ["Mock"], Disciplines = configuredDisciplines ?? [] };
        var factory = new EvidenceProviderFactory([new MockEvidenceProvider()]);
        return new AnalysisPipeline(
            scanner,
            factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator(analyzers),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            repository);
    }

    [Fact]
    public async Task Pipeline_fails_fast_before_scan_for_an_unregistered_discipline()
    {
        var scanner = new ThrowingScanner();
        var repository = new ThrowingRepository();
        var pipeline = PipelineWith(scanner, repository, null, new SecurityAnalyzer());

        var ex = await Assert.ThrowsAsync<UnsupportedDisciplineException>(() =>
            pipeline.RunAsync(new AnalysisRequest
            {
                TargetPath = "/repo",
                Disciplines = [FindingCategory.Performance]
            }));

        Assert.Equal([FindingCategory.Performance], ex.Unsupported);
        Assert.False(scanner.Invoked);    // before repository scan
        Assert.False(repository.Invoked); // nothing persisted
    }

    [Fact]
    public async Task Pipeline_fails_fast_for_config_driven_unregistered_discipline()
    {
        var scanner = new ThrowingScanner();
        var repository = new ThrowingRepository();
        var pipeline = PipelineWith(scanner, repository, [FindingCategory.Maintainability], new SecurityAnalyzer());

        var ex = await Assert.ThrowsAsync<UnsupportedDisciplineException>(() =>
            pipeline.RunAsync(new AnalysisRequest { TargetPath = "/repo" }));

        Assert.Equal([FindingCategory.Maintainability], ex.Unsupported);
        Assert.False(scanner.Invoked);
        Assert.False(repository.Invoked);
    }

    [Fact]
    public async Task Pipeline_refuses_to_run_with_no_registered_analyzers()
    {
        var scanner = new ThrowingScanner();
        var repository = new ThrowingRepository();
        var pipeline = PipelineWith(scanner, repository, null /* no analyzers */);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RunAsync(new AnalysisRequest { TargetPath = "/repo" }));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(scanner.Invoked);
        Assert.False(repository.Invoked);
    }

    [Fact]
    public async Task Pipeline_does_not_fail_fast_for_supported_disciplines()
    {
        var scanner = new ThrowingScanner();
        var repository = new ThrowingRepository();
        var pipeline = PipelineWith(scanner, repository, null, new SecurityAnalyzer());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RunAsync(new AnalysisRequest
            {
                TargetPath = "/repo",
                Disciplines = [FindingCategory.Security]
            }));

        // The discipline check passed; the run progressed far enough to hit the
        // throwing scanner (i.e. it did NOT fail fast on the discipline).
        Assert.True(scanner.Invoked);
    }
}
