using System.Text.Json;
using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.1 — bounded parallel execution of INDEPENDENT acquisition steps.
/// Independent provider steps may overlap up to EvidenceOptions.MaxConcurrency while
/// the final Evidence/records are ALWAYS aggregated in plan order (never completion
/// order); MaxConcurrency=1 is strictly sequential; provider failure isolation,
/// provider timeout vs run cancellation, and provenance are unchanged; the downstream
/// pipeline and the package contract are untouched. All providers are deterministic
/// fakes driven by TaskCompletionSource gates — no network, no credentials, no long sleeps.
/// </summary>
public sealed class ParallelAcquisitionTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-par-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-par-out-" + Guid.NewGuid().ToString("N"));

    public ParallelAcquisitionTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Service.cs"), "public class Service { }\n");
    }

    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        Files = [new ScannedFile { RelativePath = "src/App.cs", Extension = ".cs", SizeBytes = 1, LineCount = 10, Content = "public class App { }" }]
    };

    // ── Conservative default + plan wiring ─────────────────────────────────────

    [Fact]
    public void Default_MaxConcurrency_is_sequentially_conservative()
    {
        Assert.Equal(1, new EvidenceOptions().MaxConcurrency);
        Assert.Equal(1, new CouncilOptions().MaxConcurrency);   // DI default is also sequential
    }

    [Fact] // 1
    public async Task Independent_steps_overlap_in_time()
    {
        var meter = new ConcurrencyMeter();
        var providers = Enumerable.Range(0, 3).Select(i => new GateProvider($"P{i}", meter)).ToArray();
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var run = executor.ExecuteAsync(plan, Snapshot);
        await WaitAllStarted(providers);                       // all 3 entered while gates are held

        foreach (var p in providers) p.Release.TrySetResult();
        var result = await run;

        Assert.Equal(3, result.Executions.Count);
        Assert.Equal(3, result.Evidence.Count);
        Assert.Equal(3, meter.Peak);                           // provably overlapped, not serialized
    }

    [Fact] // 2
    public async Task MaxConcurrency_one_preserves_sequential_execution()
    {
        var meter = new ConcurrencyMeter();
        var p = Enumerable.Range(0, 3).Select(i => new GateProvider($"P{i}", meter)).ToArray();
        var plan = Plan(p);
        var executor = Executor(maxConcurrency: 1, p);

        var run = executor.ExecuteAsync(plan, Snapshot);

        await p[0].Started.Task;
        Assert.False(p[1].Started.Task.IsCompleted);           // strictly sequential: P1 cannot start yet
        Assert.Equal(1, meter.Peak);
        p[0].Release.TrySetResult();

        await p[1].Started.Task;
        Assert.False(p[2].Started.Task.IsCompleted);
        Assert.Equal(1, meter.Peak);
        p[1].Release.TrySetResult();

        await p[2].Started.Task;
        p[2].Release.TrySetResult();

        var result = await run;
        Assert.Equal(1, meter.Peak);
        Assert.Equal(["P0", "P1", "P2"], result.Executions.Select(r => r.ProviderName));
    }

    [Fact] // 3
    public async Task MaxConcurrency_two_never_exceeds_two_active_steps()
    {
        var meter = new ConcurrencyMeter();
        var p = Enumerable.Range(0, 4).Select(i => new GateProvider($"P{i}", meter)).ToArray();
        var plan = Plan(p);
        var executor = Executor(maxConcurrency: 2, p);

        var run = executor.ExecuteAsync(plan, Snapshot);

        await WaitUntilAsync(() => meter.Peak == 2);           // exactly two active, held by gates
        Assert.False(p[2].Started.Task.IsCompleted);           // the third is queued, not started
        Assert.Equal(2, meter.Peak);

        foreach (var provider in p) provider.Release.TrySetResult();
        var result = await run;

        Assert.Equal(4, result.Executions.Count);
        Assert.Equal(2, meter.Peak);                           // bounded: never exceeded 2
        Assert.False(meter.Exceeded(2));
    }

    [Fact] // 4
    public async Task MaxConcurrency_three_allows_three_independent_steps()
    {
        var meter = new ConcurrencyMeter();
        var providers = Enumerable.Range(0, 3).Select(i => new GateProvider($"P{i}", meter)).ToArray();
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var run = executor.ExecuteAsync(plan, Snapshot);
        await WaitAllStarted(providers);

        foreach (var p in providers) p.Release.TrySetResult();
        var result = await run;

        Assert.Equal(3, result.Executions.Count);
        Assert.Equal(3, meter.Peak);
        Assert.False(meter.Exceeded(3));
    }

    [Fact] // 5
    public async Task Results_follow_plan_order_not_completion_order()
    {
        // P0 is FIRST in plan but finishes LAST; P1/P2 complete while P0 is still held.
        var gated = new GateProvider("P0");
        var fast1 = new InstantProvider("P1");
        var fast2 = new InstantProvider("P2");
        var providers = new IEvidenceProvider[] { gated, fast1, fast2 };
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var run = executor.ExecuteAsync(plan, Snapshot);

        await gated.Started.Task;
        await Task.WhenAll(fast1.Served.Task, fast2.Served.Task); // the LATER plan steps already completed
        gated.Release.TrySetResult();
        var result = await run;

        // Deterministic aggregation must be plan order — never completion order.
        Assert.Equal(["P0", "P1", "P2"], result.Evidence.Select(e => e.ProviderName));
        Assert.Equal(["P0", "P1", "P2"], result.Executions.Select(r => r.ProviderName));
        Assert.Equal(plan.Steps.Select(s => s.StepId), result.Executions.Select(r => r.StepId));
    }

    [Fact] // 6
    public async Task Execution_telemetry_ordering_is_deterministic()
    {
        var first = await RunParallelOnce("A", "B", "C");
        var second = await RunParallelOnce("A", "B", "C");

        Assert.Equal(first, second);                           // same StepId/evidence trace, same order
    }

    [Fact] // 7
    public async Task Continue_isolates_one_failed_provider()
    {
        IEvidenceProvider[] providers =
        [
            new InstantProvider("Open"),
            new ThrowProvider("Codex"),
            new InstantProvider("Coda")
        ];
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var result = await executor.ExecuteAsync(plan, Snapshot);

        var codexCalls = result.Executions.Where(r => r.ProviderName == "Codex").ToList();
        var codexEvidence = result.Evidence.Where(e => e.ProviderName == "Codex").ToList();
        Assert.Single(codexCalls);
        Assert.False(codexCalls[0].Success);
        Assert.False(codexEvidence[0].Success);
        Assert.Contains("kaboom", codexCalls[0].ErrorMessage);

        // The other two succeeded and their evidence survived.
        Assert.All(result.Executions.Where(r => r.ProviderName != "Codex"), r => Assert.True(r.Success));
        Assert.Equal(2, result.Evidence.Count(e => e.Success));
        Assert.Equal(3, result.Executions.Count);              // every step produced exactly one record
    }

    [Fact] // 8
    public async Task FailRun_mode_is_preserved_with_a_parallel_executor()
    {
        // FailRun is a pipeline policy: the executor records the failure; the pipeline
        // aborts the whole run. Asserted end to end with two steps running concurrently.
        var providers = new IEvidenceProvider[]
        {
            new InstantProvider("Ok"),
            new UnavailableProvider("Down")
        };
        var pipeline = BuildPipeline(providers, maxConcurrency: 2, ProviderFailureMode.FailRun);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Ok, Down" });

        Assert.Equal(AnalysisRunStatus.Failed, result.Run.Status);
        Assert.Contains("FailRun", result.Run.Error);
        Assert.Contains("Down", result.Run.Error);
    }

    [Fact] // 9
    public async Task Run_cancellation_cancels_active_work_and_propagates()
    {
        using var cts = new CancellationTokenSource();
        var providers = Enumerable.Range(0, 3)
            .Select(i => new CancellationWaiterProvider($"P{i}")).Cast<IEvidenceProvider>().ToArray();
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var run = executor.ExecuteAsync(plan, Snapshot, cts.Token);
        await WaitAllEntered(providers.Cast<CancellationWaiterProvider>());
        await cts.CancelAsync();

        // Cancellation propagates as OCE (NEVER a failed evidence / a completed result)
        // and waits for the active steps first.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact] // 10
    public async Task Cancellation_prevents_queued_work_from_starting()
    {
        using var cts = new CancellationTokenSource();
        IEvidenceProvider[] providers = [new CancellationWaiterProvider("P0"), new CancellationWaiterProvider("P1"), new CancellationWaiterProvider("P2")];
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 1, providers);   // P1/P2 are queued behind P0

        var run = executor.ExecuteAsync(plan, Snapshot, cts.Token);
        await ((CancellationWaiterProvider)providers[0]).Entered.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.False(((CancellationWaiterProvider)providers[1]).Entered.Task.IsCompleted); // never started
        Assert.False(((CancellationWaiterProvider)providers[2]).Entered.Task.IsCompleted);
    }

    [Fact] // 11
    public async Task Provider_timeout_stays_distinct_from_cancellation()
    {
        using var cts = new CancellationTokenSource();
        IEvidenceProvider[] providers = [new CancellationWaiterProvider("Slow"), new InstantProvider("Fast")];
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 2, providers, timeout: TimeSpan.FromMilliseconds(150));

        var result = await executor.ExecuteAsync(plan, Snapshot, cts.Token);

        Assert.False(cts.IsCancellationRequested);              // run was never cancelled
        var slow = Assert.Single(result.Executions, r => r.ProviderName == "Slow");
        Assert.False(slow.Success);
        Assert.Equal("Timeout", slow.ErrorCategory);            // a TIMEOUT, not a cancellation
        var fast = Assert.Single(result.Executions, r => r.ProviderName == "Fast");
        Assert.True(fast.Success);                              // Continue semantics: the run kept going
    }

    [Fact] // 12
    public async Task Parallel_execution_does_not_duplicate_steps()
    {
        IEvidenceProvider[] providers = [new InstantProvider("A"), new InstantProvider("B"), new InstantProvider("C")];
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var result = await executor.ExecuteAsync(plan, Snapshot);

        Assert.Equal(3, result.Evidence.Count);
        Assert.Equal(3, result.Executions.Count);
        Assert.Equal(plan.Steps.Select(s => s.StepId), result.Executions.Select(r => r.StepId).Distinct());
        Assert.Equal(plan.Steps.Count, result.Executions.Select(r => r.StepId).Distinct().Count());
    }

    [Fact] // 13
    public async Task AcquisitionStepId_and_correlation_provenance_is_preserved()
    {
        IEvidenceProvider[] providers = [new InstantProvider("Open"), new InstantProvider("Codex"), new InstantProvider("Claude")];
        var plan = Plan(providers);
        var executor = Executor(maxConcurrency: 3, providers);

        var result = await executor.ExecuteAsync(plan, Snapshot);

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            Assert.All(result.Evidence.Where(e => e.AcquisitionStepId == step.StepId), e => Assert.Equal(step.ProviderName, e.ProviderName));
            Assert.All(result.Executions.Where(r => r.StepId == step.StepId), r => Assert.Equal(step.CorrelationId, r.CorrelationId));
        }
    }

    [Fact] // 14
    public async Task Package_and_consumer_contract_are_unchanged_by_parallelism()
    {
        var providers = Enumerable.Range(0, 3)
            .Select(i => new LlmObservationProvider($"Agent{i}", Observations("Security", "HardcodedSecret", $"Secret observed by Agent{i}")))
            .Cast<IEvidenceProvider>().ToArray();
        var pipeline = BuildPipeline(providers, maxConcurrency: 3, ProviderFailureMode.Continue);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Agent0, Agent1, Agent2" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);
        Assert.Equal(3, result.Run.ProviderExecution!.TotalExecutions);

        var json = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;

        Assert.Equal("1.1", dto.SchemaVersion);                // still 1.1 — no domain-contract change
        Assert.NotEmpty(dto.Findings);
        Assert.DoesNotContain("maxConcurrency", json);         // parallelism is an execution concern, not contract
        Assert.DoesNotContain("MaxConcurrency", json);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────

    private static EvidenceAcquisitionPlan Plan(IReadOnlyCollection<IEvidenceProvider> providers)
        => new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, providers,
            [FindingCategory.Security]);

    private static EvidenceAcquisitionExecutor Executor(
        int maxConcurrency, IReadOnlyList<IEvidenceProvider> providers, TimeSpan? timeout = null)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = timeout ?? TimeSpan.FromSeconds(5), MaxConcurrency = maxConcurrency });

    private async Task<IReadOnlyList<string>> RunParallelOnce(params string[] names)
    {
        var providers = names.Select(n => (IEvidenceProvider)new InstantProvider(n)).ToArray();
        var plan = Plan(providers);
        var result = await Executor(maxConcurrency: 3, providers).ExecuteAsync(plan, Snapshot);
        return result.Executions.Select(r => $"{r.StepId}:{r.ProviderName}:{r.EvidenceCount}").ToList();
    }

    private static async Task WaitAllStarted(IEnumerable<GateProvider> providers)
    {
        var all = Task.WhenAll(providers.Select(p => p.Started.Task));
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(all, finished);
    }

    private static async Task WaitAllEntered(IEnumerable<CancellationWaiterProvider> providers)
    {
        var all = Task.WhenAll(providers.Select(p => p.Entered.Task));
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(all, finished);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("condition was not reached in time");
            await Task.Delay(20);
        }
    }

    private AnalysisPipeline BuildPipeline(
        IReadOnlyList<IEvidenceProvider> providers, int maxConcurrency, ProviderFailureMode failureMode)
    {
        var options = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security],
            ProviderFailureMode = failureMode,
            MaxConcurrency = maxConcurrency
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    private static string Observations(string discipline, string type, string title)
        => $$"""
        { "schemaVersion": "1.0", "discipline": "{{discipline}}",
          "observations": [ { "type": "{{type}}", "discipline": "{{discipline}}", "title": "{{title}}",
            "description": "Reported by an LLM evidence source.", "severity": "High", "confidence": "Medium",
            "fileReferences": [ { "path": "src/Service.cs", "startLine": 1 } ] } ] }
        """;

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Deterministic fakes ────────────────────────────────────────────────────

    /// <summary>Signals <see cref="Started"/> on entry; waits for <see cref="Release"/> (the test controls duration).</summary>
    private sealed class GateProvider(string name, ConcurrencyMeter? meter = null) : IEvidenceProvider
    {
        public string Name => name;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
            RequiresAnalyzerInstructions = false,
            SupportsRepositoryWideAnalysis = true,
            Version = "test-gate"
        };

        public async Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            using (meter?.Enter())
            {
                Started.TrySetResult();
                await Release.Task.ConfigureAwait(false);
            }
            return [new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.LLM, RawResponse = "{}" }];
        }
    }

    private sealed class InstantProvider(string name) : IEvidenceProvider
    {
        public TaskCompletionSource Served { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
            SupportsRepositoryWideAnalysis = true,
            Version = "test-instant"
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            Served.TrySetResult();
            return Task.FromResult<IReadOnlyList<Evidence>>(
                [new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.LLM, RawResponse = "{}" }]);
        }
    }

    private sealed class ThrowProvider(string name) : IEvidenceProvider
    {
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
            SupportsRepositoryWideAnalysis = true,
            Version = "test-throw"
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("kaboom");
    }

    private sealed class UnavailableProvider(string name) : IEvidenceProvider
    {
        public bool IsAvailable => false;
        public string? UnavailableReason => $"'{name}' is not configured.";
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
            SupportsRepositoryWideAnalysis = true,
            Version = "test-unavailable"
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("should not be called");
    }

    /// <summary>Enters, signals, then waits on the (linked) step token — so the executor's
    /// provider-timeout and a run cancellation both interrupt it, exactly like a real provider.</summary>
    private sealed class CancellationWaiterProvider(string name) : IEvidenceProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
            SupportsRepositoryWideAnalysis = true,
            Version = "test-waiter"
        };
        public async Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    /// <summary>Returns one LLM-style evidence envelope so the interpreter + analyzers can consume it.</summary>
    private sealed class LlmObservationProvider(string name, string rawResponse) : IEvidenceProvider
    {
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
            SupportedDisciplines = new HashSet<FindingCategory> { FindingCategory.Security },
            RequiresAnalyzerInstructions = true,
            SupportsRepositoryWideAnalysis = false,
            Version = "test-llm"
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Evidence>>(
                [new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.LLM, RawResponse = rawResponse }]);
    }

    /// <summary>Thread-safe active-step counter; Peak is the maximum simultaneous providers.</summary>
    private sealed class ConcurrencyMeter
    {
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);
        public bool Exceeded(int limit) => Peak > limit;

        public IDisposable Enter()
        {
            var active = Interlocked.Increment(ref _current);
            UpdatePeak(active);
            return new ExitGuard(this);
        }

        private void UpdatePeak(int candidate)
        {
            int observed;
            while ((observed = Volatile.Read(ref _peak)) < candidate
                   && Interlocked.CompareExchange(ref _peak, candidate, observed) != observed)
            {
                // Retry the CAS until the peak reflects this candidate.
            }
        }

        private void Exit() => Interlocked.Decrement(ref _current);

        private sealed class ExitGuard(ConcurrencyMeter meter) : IDisposable
        {
            public void Dispose() => meter.Exit();
        }
    }
}