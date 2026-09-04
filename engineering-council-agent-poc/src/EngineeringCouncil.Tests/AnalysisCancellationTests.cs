using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
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
/// Milestone 011.1 — cancellation must stop the analysis pipeline: no further
/// reconciliation, no package build, no persistence, and never a completed or
/// "ordinary failed" result. Cancellation propagates out of RunAsync as an
/// OperationCanceledException so the API never returns a review result for it.
/// </summary>
public sealed class AnalysisCancellationTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-cancel-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-cancel-out-" + Guid.NewGuid().ToString("N"));

    public AnalysisCancellationTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Service.cs"), "public class Service { }\n");
    }

    private AnalysisPipeline BuildPipeline(
        IReadOnlyList<IEvidenceProvider> providers,
        IFindingReconciler? reconciler = null,
        ICouncilSummaryGenerator? summary = null,
        IEngineeringReviewPackageBuilder? packageBuilder = null,
        IAnalysisRunRepository? repo = null)
    {
        var options = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security]
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer()]),
            reconciler ?? new RuleBasedFindingReconciler(),
            summary ?? new RuleBasedCouncilSummaryGenerator(),
            packageBuilder ?? new EngineeringReviewPackageBuilder(),
            options,
            repo ?? new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    [Fact]
    public async Task A_pre_cancelled_run_propagates_cancellation_and_does_no_work()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var reconciler = new TrackingReconciler();
        var summary = new TrackingSummaryGenerator();
        var packageBuilder = new TrackingPackageBuilder();
        var repo = new TrackingRepository();
        var pipeline = BuildPipeline([new MockEvidenceProvider()],
            reconciler, summary, packageBuilder, repo);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" }, cts.Token));

        Assert.Equal(0, reconciler.Calls);
        Assert.Equal(0, summary.Calls);
        Assert.Equal(0, packageBuilder.Calls);
        Assert.Equal(0, repo.SaveCalls);
        Assert.False(Directory.Exists(_outputs));
    }

    [Fact]
    public async Task Cancellation_during_acquisition_propagates_without_reconciliation_or_persistence()
    {
        using var cts = new CancellationTokenSource();

        var reconciler = new TrackingReconciler();
        var summary = new TrackingSummaryGenerator();
        var packageBuilder = new TrackingPackageBuilder();
        var repo = new TrackingRepository();
        var pipeline = BuildPipeline([new CancellingEvidenceProvider(cts)],
            reconciler, summary, packageBuilder, repo);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Cancelling" }, cts.Token));

        // Scan happened; acquisition cancelled mid-flight → the rest never ran.
        Assert.Equal(0, reconciler.Calls);
        Assert.Equal(0, summary.Calls);
        Assert.Equal(0, packageBuilder.Calls);
        Assert.Equal(0, repo.SaveCalls);
        Assert.False(Directory.Exists(_outputs));
    }

    [Fact]
    public async Task Cancellation_is_not_converted_into_an_ordinary_failed_run()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pipeline = BuildPipeline([new MockEvidenceProvider()]);

        // Must throw cancellation — NOT return an AnalysisResult with Failed status.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" }, cts.Token));
    }

    [Fact]
    public async Task A_normal_run_still_completes_and_persists_the_package()
    {
        var pipeline = BuildPipeline([new MockEvidenceProvider()]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Mock" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "engineering-review-package.json")));
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class TrackingReconciler : IFindingReconciler
    {
        public int Calls { get; private set; }

        public ReconciliationResult Reconcile(
            IReadOnlyList<Finding> rawFindings, IReadOnlyList<EngineeringObservation> observations,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return new RuleBasedFindingReconciler().Reconcile(rawFindings, observations, cancellationToken);
        }
    }

    private sealed class TrackingSummaryGenerator : ICouncilSummaryGenerator
    {
        public int Calls { get; private set; }

        public Task<CouncilSummary> GenerateAsync(
            AnalysisRun run, IReadOnlyList<Finding> consolidatedFindings, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new RuleBasedCouncilSummaryGenerator().GenerateAsync(run, consolidatedFindings, cancellationToken);
        }
    }

    private sealed class TrackingPackageBuilder : IEngineeringReviewPackageBuilder
    {
        public int Calls { get; private set; }

        public EngineeringReviewPackage Build(AnalysisRun run)
        {
            Calls++;
            return new EngineeringReviewPackageBuilder().Build(run);
        }
    }

    private sealed class TrackingRepository : IAnalysisRunRepository
    {
        public int SaveCalls { get; private set; }

        public Task<string> SaveAsync(AnalysisRun run, EngineeringReviewPackage package, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult("unused");
        }

        public Task<AnalysisRun?> GetAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult<AnalysisRun?>(null);

        public Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>Cancels the run token while a call is in flight, then throws.</summary>
    private sealed class CancellingEvidenceProvider : IEvidenceProvider
    {
        private readonly CancellationTokenSource _cts;

        public CancellingEvidenceProvider(CancellationTokenSource cts) => _cts = cts;

        public bool IsAvailable => true;

        public EvidenceProviderMetadata Metadata => new()
        {
            Name = "Cancelling",
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequiresAnalyzerInstructions = true,
            SupportsRepositoryWideAnalysis = false,
            Version = "test"
        };

        public async Task<IReadOnlyList<Evidence>> CollectAsync(
            EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            _cts.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
