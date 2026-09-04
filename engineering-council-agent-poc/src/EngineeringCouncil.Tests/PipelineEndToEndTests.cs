using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Merging;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class PipelineEndToEndTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-out-" + Guid.NewGuid().ToString("N"));

    public PipelineEndToEndTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Service.cs"),
            "public class Service { public int Add(int a, int b) => a + b; }\n");
    }

    [Fact]
    public async Task Hybrid_pipeline_plan_acquire_interpret_analyze_package_end_to_end()
    {
        var options = new EvidenceOptions { Providers = ["Mock"] };
        var factory = new EvidenceProviderFactory([new MockEvidenceProvider()]);
        var planner = new EvidenceAcquisitionPlanner();
        var executor = new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options);
        var interpretation = new EvidenceInterpretationPipeline(
            new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()]));

        var orchestrator = new AnalysisOrchestrator(
            [new ArchitectureAnalyzer(), new SecurityAnalyzer(), new TestingAnalyzer()]);

        var repo = new FileSystemAnalysisRunRepository(
            new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
            new EngineeringReviewMarkdownExporter(),
            new JsonReportGenerator());

        var pipeline = new AnalysisPipeline(
            new FileSystemRepositoryScanner(),
            factory,
            planner, executor, interpretation, orchestrator,
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            repo);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);
        Assert.Null(result.Run.Error);

        // Mock is Discipline-scoped → one step per selected discipline (3 analyzers).
        Assert.NotNull(result.Run.AcquisitionPlan);
        Assert.Equal(3, result.Run.AcquisitionPlan!.Steps.Count);
        Assert.Equal(3, result.Run.AcquisitionPlan.DisciplineScopedSteps);
        Assert.Equal(3, result.Run.ProviderExecution!.TotalExecutions);

        // One observation per discipline step, each with acquisition provenance.
        Assert.Equal(3, result.Run.Observations.Count);
        Assert.All(result.Run.Observations, o =>
        {
            Assert.Equal(EvidenceAcquisitionScope.Discipline, o.AcquisitionScope);
            Assert.NotNull(o.RequestedDiscipline);
            Assert.False(string.IsNullOrEmpty(o.AcquisitionCorrelationId));
        });

        // Three analyzers → one finding each; provenance back to observations/providers.
        Assert.Equal(3, result.Run.Findings.Count);
        Assert.All(result.Run.Findings, f =>
        {
            Assert.NotEmpty(f.ObservationIds);
            Assert.Contains("Mock", f.SupportingProviders);
        });

        // Package carries the acquisition plan + observation metrics.
        var package = result.Package;
        Assert.NotNull(package.AcquisitionPlan);
        Assert.Equal(3, package.Metrics.TotalObservations);
        Assert.Equal(EngineeringHealth.Good, package.OverallEngineeringHealth);

        foreach (var file in new[]
        {
            "engineering-review-package.json", "engineering-review.md", "observations.json",
            "findings.json", "raw-findings.json", "provider-execution.json", "run.json"
        })
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, file)), $"missing {file}");
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
