using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Llm;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3B — bounded acquisition-level retry. A logical step gets at most
/// ONE retry (Evidence:Execution:MaxAttempts, default 2) and ONLY for a retry-eligible
/// (Timeout) failure. The retry reuses the SAME effective provider timeout, belongs to
/// the same logical step (never bypasses MaxConcurrency) and never runs a third attempt.
/// Cancellation, authentication/configuration, schema validation and unexpected errors
/// are never retried. Telemetry: AttemptCount, RetryCount (step-level retries plus the
/// final attempt's provider-internal retries) and RetryExhausted. Coverage (M15.3A)
/// stays authoritative: a retried success is successful evidence; an exhausted retry is
/// NoEvidence. All tests are offline, deterministic and short.
/// </summary>
public sealed class BoundedRetryTests
{
    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        ProjectFiles = ["src/App.csproj"],
        Files =
        [
            new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 40, Content = "public class Auth { void Authorize(){} }" },
        ]
    };

    private static FindingCategory Security => FindingCategory.Security;

    private static Evidence Evidence(string providerName) => new() { ProviderName = providerName, Success = true };

    // ── 1. Success on the first attempt ──────────────────────────────────────

    [Fact]
    public async Task First_try_success_records_attempt_one_and_no_retry()
    {
        var probe = new RetryProbe("P", timeout: null, (_, _) => Task.FromResult<IReadOnlyList<Evidence>>([Evidence("P")]));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.True(record.Success);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(0, record.RetryCount);
        Assert.False(record.RetryExhausted);
        Assert.Equal(1, probe.CallCount);
    }

    // ── 2. Timeout then success: exactly one bounded retry ───────────────────

    [Fact]
    public async Task Executor_timeout_then_success_is_retried_once()
    {
        // Attempt 1 exceeds the provider's OWN 150ms timeout → executor-detected Timeout.
        // Attempt 2 succeeds. The step must recover with exactly one retry.
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(150), async (attempt, ct) =>
        {
            if (attempt == 1)
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            return [Evidence("P")];
        });

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.True(record.Success, "a bounded retry must let a slow-but-healthy step recover");
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(1, record.RetryCount);
        Assert.False(record.RetryExhausted);

        var ev = Assert.Single(result.Evidence);
        Assert.True(ev.Success);
        Assert.Equal(2, ev.AttemptCount);
        Assert.Equal(1, ev.RetryCount);
        Assert.False(ev.RetryExhausted);
    }

    [Fact]
    public async Task Provider_reported_timeout_then_success_is_retried_once()
    {
        // A provider-reported LlmProviderException(Timeout) (e.g. an agentic process that
        // exceeded its own bound) is equally retry-eligible.
        var probe = new RetryProbe("P", timeout: null, (attempt, _) =>
            attempt == 1
                ? throw new LlmProviderException(LlmErrorCategory.Timeout, "provider timed out")
                : Task.FromResult<IReadOnlyList<Evidence>>([Evidence("P")]));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.True(record.Success);
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(1, record.RetryCount);
        Assert.False(record.RetryExhausted);
    }

    // ── 3. Timeout then timeout: exhausted, reported honestly ────────────────

    [Fact]
    public async Task Timeout_then_timeout_exhausts_and_reports_retry_exhausted()
    {
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(150),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(300), ct); return [Evidence("P")]; });

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("Timeout", record.ErrorCategory);
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(1, record.RetryCount);
        Assert.True(record.RetryExhausted);

        var ev = Assert.Single(result.Evidence);
        Assert.False(ev.Success);
        Assert.Equal("Timeout", ev.ErrorCategory);
        Assert.True(ev.RetryExhausted);
    }

    [Fact]
    public async Task A_third_attempt_is_never_attempted()
    {
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(100),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); return [Evidence("P")]; });

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);   // 1 initial + exactly 1 retry, never a third
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.True(record.RetryExhausted);
    }

    [Fact]
    public async Task MaxAttempts_one_disables_step_retry()
    {
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(100),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); return [Evidence("P")]; });

        var result = await Executor(new EvidenceOptions { MaxAttempts = 1 }, probe)
            .ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(1, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(0, record.RetryCount);
        Assert.True(record.RetryExhausted, "a Timeout on the only allowed attempt exhausted the retry budget");
    }

    // ── 4. The retry reuses the SAME effective timeout (no doubling) ──────────

    [Fact]
    public async Task Retry_reuses_the_same_effective_timeout()
    {
        // Provider timeout 200ms; each attempt needs 700ms. With a constant 200ms
        // per-attempt bound both attempts time out at ~200ms → total ~400ms. If the
        // retry doubled the budget (400ms) the total would be ~600ms.
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(200),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(700), ct); return [Evidence("P")]; });

        var result = await Executor(new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(60) }, probe)
            .ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.True(record.Duration < TimeSpan.FromMilliseconds(550),
            $"the retry must reuse the SAME 200ms bound, not grow it (Duration was {record.Duration})");
        Assert.True(record.RetryExhausted);
    }

    // ── 10. Duration semantics: total logical‑step wall‑clock including retries ──
    [Fact]
    public async Task Duration_is_total_logical_step_wall_clock_including_retries()
    {
        // Provider timeout 150ms; each attempt delays 300ms → each attempt times out at ~150ms.
        // With continuous stopwatch the total Duration should be ~300ms (two attempts).
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(150),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(300), ct); return [Evidence("P")]; });

        var result = await Executor(new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(60) }, probe)
            .ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(1, record.RetryCount);
        Assert.True(record.RetryExhausted);
        // total wall‑clock should be roughly two timeouts (~300ms) with small overhead
        Assert.True(record.Duration >= TimeSpan.FromMilliseconds(250),
            $"Duration should include both attempts (was {record.Duration})");
        Assert.True(record.Duration <= TimeSpan.FromMilliseconds(500),
            $"Duration should not exceed reasonable overhead (was {record.Duration})");
    }

    // ── 5. Never-retried failures ────────────────────────────────────────────

    [Fact]
    public async Task Authentication_failure_is_never_retried()
    {
        var probe = new RetryProbe("P", timeout: null, (_, _) =>
            throw new LlmProviderException(LlmErrorCategory.Authentication, "invalid api key"));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(1, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("Authentication", record.ErrorCategory);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(0, record.RetryCount);
        Assert.False(record.RetryExhausted);
    }

    [Fact]
    public async Task Configuration_failure_is_never_retried()
    {
        var probe = new RetryProbe("P", timeout: null, (_, _) =>
            throw new LlmProviderException(LlmErrorCategory.Configuration, "not configured"));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(1, probe.CallCount);
        Assert.Equal("Configuration", Assert.Single(result.Executions).ErrorCategory);
        Assert.False(Assert.Single(result.Executions).RetryExhausted);
    }

    [Fact]
    public async Task Schema_validation_failure_is_never_retried()
    {
        var probe = new RetryProbe("P", timeout: null, (_, _) =>
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation, "invalid schema"));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(1, probe.CallCount);
        Assert.Equal("SchemaValidation", Assert.Single(result.Executions).ErrorCategory);
        Assert.False(Assert.Single(result.Executions).RetryExhausted);
    }

    [Fact]
    public async Task Unexpected_exception_is_never_retried()
    {
        var probe = new RetryProbe("P", timeout: null, (_, _) => throw new InvalidOperationException("boom"));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(1, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Null(record.ErrorCategory);
        Assert.False(record.RetryExhausted);
    }

    [Fact]
    public async Task Run_cancellation_is_never_retried()
    {
        var probe = new RetryProbe("P", TimeSpan.FromSeconds(60),
            async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return [Evidence("P")]; });

        using var cts = new CancellationTokenSource();
        var task = Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.True(probe.CallCount == 1, "cancellation propagates; it is never turned into a retry");
    }

    // ── 6. Concurrency: a retry stays inside its logical step's permit ────────

    [Fact]
    public async Task Retry_stays_within_the_logical_steps_concurrency_permit()
    {
        var maxActive = 0;
        var active = 0;
        var gate = new object();

        RetryProbe WithCounter(string name) => new(name, TimeSpan.FromMilliseconds(100), async (attempt, ct) =>
        {
            var n = Interlocked.Increment(ref active);
            lock (gate) { if (n > maxActive) maxActive = n; }
            try
            {
                if (attempt == 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(300), ct);   // times out → retry
                return [Evidence(name)];
            }
            finally { Interlocked.Decrement(ref active); }
        });

        var p0 = WithCounter("P0");
        var p1 = WithCounter("P1");
        var result = await Executor(new EvidenceOptions { MaxConcurrency = 1, ProviderTimeout = TimeSpan.FromSeconds(60) }, p0, p1)
            .ExecuteAsync(Plan([p0, p1], [Security]), Snapshot);

        Assert.True(maxActive == 1, "a retry never opens a second parallel slot; it stays inside its step's permit");
        Assert.All(result.Executions, r => Assert.True(r.Success));
        Assert.All(result.Executions, r => Assert.Equal(2, r.AttemptCount));
    }

    // ── 7. Telemetry: step-level retries + final attempt's provider-internal retries ──

    [Fact]
    public async Task Provider_internal_retries_are_counted_alongside_step_retries()
    {
        // Attempt 1: provider-reported Timeout with 2 internal retries already made.
        // Attempt 2: succeeds, the client reported 3 internal retries.
        // RetryCount = step-level retries (1) + the FINAL attempt's internal retries (3).
        var probe = new RetryProbe("P", timeout: null, (attempt, _) =>
            attempt == 1
                ? throw new LlmProviderException(LlmErrorCategory.Timeout, "timed out", retryCount: 2)
                : Task.FromResult<IReadOnlyList<Evidence>>(
                    [new Evidence { ProviderName = "P", Success = true, RetryCount = 3 }]));

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);

        Assert.Equal(2, probe.CallCount);
        var record = Assert.Single(result.Executions);
        Assert.True(record.Success);
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(4, record.RetryCount);
        Assert.False(record.RetryExhausted);
        Assert.Equal(4, Assert.Single(result.Evidence).RetryCount);
    }

    // ── 8. Coverage interaction (M15.3A stays authoritative) ─────────────────

    [Fact]
    public async Task Retried_success_counts_as_successful_coverage()
    {
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(150), async (attempt, ct) =>
        {
            if (attempt == 1)
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            return [Evidence("P")];
        });

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);
        var report = ProviderExecutionReport.FromRecords(result.Executions);

        var coverage = DisciplineCoverage.From(report, [], [Security]);
        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.CoveredNoFindings, entry.Status);
        Assert.Equal(1, entry.SuccessfulProviders);
        Assert.Equal(1, entry.AttemptedProviders);
    }

    [Fact]
    public async Task Exhausted_retry_counts_as_no_evidence()
    {
        var probe = new RetryProbe("P", TimeSpan.FromMilliseconds(150),
            async (_, ct) => { await Task.Delay(TimeSpan.FromMilliseconds(300), ct); return [Evidence("P")]; });

        var result = await Executor(defaultOptions(), probe).ExecuteAsync(Plan([probe], [Security]), Snapshot);
        var report = ProviderExecutionReport.FromRecords(result.Executions);

        var coverage = DisciplineCoverage.From(report, [], [Security]);
        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, entry.Status);
        Assert.Equal(0, entry.SuccessfulProviders);
        Assert.Equal(1, entry.AttemptedProviders);
    }

    // ── 9. Configuration binding ─────────────────────────────────────────────

    [Fact]
    public void MaxAttempts_defaults_and_shipped_config_keys_are_bound()
    {
        // Default: one initial attempt + one bounded retry, at both surfaces.
        Assert.Equal(2, new EvidenceOptions().MaxAttempts);
        Assert.Equal(2, new CouncilOptions().MaxAttempts);

        // The shipped CLI + API configs carry the bound key (same convention as
        // Evidence:Execution:MaxConcurrency / ProviderTimeout).
        var cli = FindAppSettings("src", "EngineeringCouncil.Cli");
        var api = FindAppSettings("src", "EngineeringCouncil.Api");
        if (cli is not null) Assert.Equal(2, ExecutionMaxAttempts(cli));
        if (api is not null) Assert.Equal(2, ExecutionMaxAttempts(api));
    }

    private static int ExecutionMaxAttempts(string appSettingsPath)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath), options);
        return doc.RootElement.GetProperty("Evidence").GetProperty("Execution").GetProperty("MaxAttempts").GetInt32();
    }

    private static string? FindAppSettings(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        if (dir is null) return null;
        var parts = new[] { dir.FullName }.Concat(segments).Append("appsettings.json").ToArray();
        var candidate = Path.Combine(parts);
        return File.Exists(candidate) ? candidate : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(EvidenceOptions options, params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(), options);

    private static EvidenceOptions defaultOptions()
        => new() { ProviderTimeout = TimeSpan.FromSeconds(60) };

    private sealed class RetryProbe : IEvidenceProvider
    {
        private readonly EvidenceProviderMetadata _metadata;
        private readonly Func<int, CancellationToken, Task<IReadOnlyList<Evidence>>> _handler;

        public RetryProbe(string name, TimeSpan? timeout, Func<int, CancellationToken, Task<IReadOnlyList<Evidence>>> handler)
        {
            _metadata = new EvidenceProviderMetadata
            {
                Name = name,
                ProviderType = EvidenceProviderType.Agentic,
                DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
                Timeout = timeout
            };
            _handler = handler;
        }

        public int CallCount => _callCount;
        private int _callCount;

        public EvidenceProviderMetadata Metadata => _metadata;
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public async Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _callCount);
            return await _handler(attempt, cancellationToken);
        }
    }
}