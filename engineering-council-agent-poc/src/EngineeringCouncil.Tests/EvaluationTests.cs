using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evaluation;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 012 — the evaluation/calibration framework. Verifies metric collection,
/// report generation, that measuring never changes the package, and that the dataset
/// manifests load. It never asserts model wording and never ranks providers.
/// </summary>
public sealed class EvaluationTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-eval-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-eval-out-" + Guid.NewGuid().ToString("N"));

    public EvaluationTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Service.cs"), "public class Service { public int Add(int a, int b) => a + b; }\n");
    }

    private AnalysisPipeline BuildMockPipeline()
    {
        var options = new EvidenceOptions { Providers = ["Mock"] };
        var factory = new EvidenceProviderFactory([new MockEvidenceProvider()]);
        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new ArchitectureAnalyzer(), new SecurityAnalyzer(), new TestingAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    private async Task<(AnalysisResult Result, EvaluationRun Metrics)> EvaluateAsync()
    {
        var result = await BuildMockPipeline().RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" });
        var repository = new EvaluationRepository { Name = "fixture", Path = _repo, Purpose = "unit test" };
        return (result, EvaluationMetricsCollector.Collect(repository, ["Mock"], result));
    }

    // ── Metric collection ─────────────────────────────────────────────────────

    [Fact]
    public async Task Metrics_reflect_the_run_without_changing_the_package()
    {
        var (result, metrics) = await EvaluateAsync();

        Assert.Equal("fixture", metrics.Repository);
        Assert.Equal("Completed", metrics.Status);
        Assert.Equal(result.Run.Observations.Count, metrics.Quality.Observations);
        Assert.Equal(result.Run.RawFindings.Count, metrics.Reconciliation.RawFindings);
        Assert.Equal(result.Package.Findings.Count, metrics.Reconciliation.ConsolidatedFindings);

        // Package validation invariants.
        Assert.Equal("1.1", metrics.Package.SchemaVersion);
        Assert.True(metrics.Package.RequiredRootPropertiesPresent);
        Assert.True(metrics.Package.DeterministicSerialization);
        Assert.True(metrics.Package.ContainsNoRuntimeTypeLeaks);
        Assert.True(metrics.Package.SizeBytes > 0);

        // Collecting metrics is read-only: the produced package is unchanged.
        var rebuilt = new EngineeringReviewPackageBuilder().Build(result.Run);
        Assert.Equal(result.Package.Findings.Count, rebuilt.Findings.Count);
        Assert.Equal(result.Package.SchemaVersion, rebuilt.SchemaVersion);
    }

    [Fact]
    public async Task Operational_timings_are_captured()
    {
        var (result, metrics) = await EvaluateAsync();

        Assert.NotNull(result.Run.StageTimings);
        Assert.True(metrics.Operational.TotalMs >= 0);
        Assert.Contains(metrics.Operational.SlowestStage,
            new[] { "Scan", "Acquisition", "Interpretation", "Analysis", "Reconciliation", "CouncilSummary", "PackageBuild", "Persistence" });
    }

    [Fact]
    public async Task Cost_is_absent_when_no_pricing_is_configured()
    {
        var (_, metrics) = await EvaluateAsync();

        Assert.Null(metrics.Usage.EstimatedCost);
        Assert.False(metrics.Usage.CostIsEstimated);
        Assert.Equal("no pricing configured", metrics.Usage.CostUnavailableReason);
    }

    [Fact]
    public async Task Cost_is_estimated_only_when_pricing_and_usage_are_both_present()
    {
        var result = await BuildMockPipeline().RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" });

        // Mock reports no token usage → even with pricing, cost stays unavailable (never invented).
        var pricing = new Dictionary<string, ModelPricing> { ["mock-observer-v2"] = new(1m, 2m) };
        var metrics = EvaluationMetricsCollector.Collect(
            new EvaluationRepository { Name = "fixture", Path = _repo }, ["Mock"], result, pricing);

        Assert.Null(metrics.Usage.EstimatedCost);
        Assert.Contains("token usage", metrics.Usage.CostUnavailableReason);
    }

    // ── Report generation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Report_presents_measurements_without_ranking_providers()
    {
        var (_, metrics) = await EvaluateAsync();
        var report = new EvaluationReport
        {
            Runs = [metrics],
            RepositoriesEvaluated = ["fixture"],
            ProvidersEvaluated = ["Mock"]
        };

        var markdown = new EvaluationReportExporter().Export(report);

        Assert.Contains("# Evaluation Report", markdown);
        Assert.Contains("Measurements only", markdown);
        foreach (var section in new[] { "## Provider comparison", "## Context selection", "## Prompt calibration",
            "## Reconciliation calibration", "## Operational metrics", "## Package validation" })
            Assert.Contains(section, markdown);

        // Never declares a winner / ranking / weighting.
        foreach (var forbidden in new[] { "winner", "best provider", "ranked #", "recommended provider" })
            Assert.DoesNotContain(forbidden, markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_evaluation_error_is_recorded_not_thrown()
    {
        // A pipeline factory that throws must not crash the runner; the case is recorded.
        var runner = new EvaluationRunner(_ => throw new InvalidOperationException("boom"));
        var repositories = new[] { new EvaluationRepository { Name = "broken", Path = _repo } };

        var report = await runner.RunAsync(repositories, [["Mock"]]);

        var run = Assert.Single(report.Runs);
        Assert.Equal("EvaluationError", run.Status);
        Assert.Contains("boom", run.Error);
    }

    // ── Dataset manifests ─────────────────────────────────────────────────────

    [Fact]
    public void The_committed_dataset_manifests_load_with_documented_purposes()
    {
        var datasetRoot = FindDatasetRoot();
        if (datasetRoot is null) return; // repo tree not locatable (packaged test run)

        var repositories = EvaluationRunner.LoadDataset(datasetRoot);
        Assert.True(repositories.Count >= 5, $"expected ≥5 dataset repositories, found {repositories.Count}");
        Assert.All(repositories, r => Assert.False(string.IsNullOrWhiteSpace(r.Purpose), $"{r.Name} has no documented purpose"));
        Assert.Contains(repositories, r => r.Sarif is not null); // the vulnerable repo ships SARIF
    }

    private static string? FindDatasetRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        var candidate = dir is null ? null : Path.Combine(dir.FullName, "evaluation", "dataset");
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
