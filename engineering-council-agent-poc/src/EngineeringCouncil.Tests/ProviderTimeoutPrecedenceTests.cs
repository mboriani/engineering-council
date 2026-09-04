using System.Text.Json;
using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
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
/// Milestone 015.2B — agentic provider timeout precedence. The acquisition executor
/// must apply the PROVIDER's own configured timeout to that provider's step, and
/// fall back to <c>Evidence:Execution:ProviderTimeout</c> only when the provider has
/// none. Timeouts stay per-step (never one shared Council timer); run cancellation
/// stays cancellation; nothing timeout-related leaks into the external package.
/// All tests are offline, deterministic and short.
/// </summary>
public sealed class ProviderTimeoutPrecedenceTests
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

    // ── 1. Provider-specific timeout overrides the global fallback ───────────

    [Fact]
    public async Task Provider_specific_timeout_overrides_global_fallback()
    {
        // Provider timeout 500ms, global fallback 150ms, step completes at 250ms.
        // If the global cap applied, the step would be cut at 150ms → Timeout.
        var provider = new FakeTimeoutProvider("P", TimeSpan.FromMilliseconds(500),
            async ct => { await Task.Delay(TimeSpan.FromMilliseconds(250), ct); return [Evidence("P")]; });

        var result = await Executor(TimeSpan.FromMilliseconds(150), provider).ExecuteAsync(Plan([provider], [Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.True(record.Success, "provider-specific 500ms timeout must win over the 150ms global fallback");
        Assert.True(record.Duration < TimeSpan.FromMilliseconds(450), $"step must finish well under its own 500ms timeout (was {record.Duration})");
        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
    }

    [Fact]
    public async Task Global_ProviderTimeout_used_when_provider_timeout_absent()
    {
        // No provider timeout → the global fallback (150ms) applies; a 250ms step times out.
        var provider = new FakeTimeoutProvider("P", timeout: null,
            async ct => { await Task.Delay(TimeSpan.FromMilliseconds(250), ct); return [Evidence("P")]; });

        var result = await Executor(TimeSpan.FromMilliseconds(150), provider).ExecuteAsync(Plan([provider], [Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("Timeout", record.ErrorCategory);
    }

    // ── 2. Effective timeout propagated per agentic provider ─────────────────

    [Fact]
    public void OpenCode_effective_timeout_is_propagated_via_metadata()
    {
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, TimeoutSeconds = 90 }, new FakeOpenCodeRunner());
        Assert.Equal(TimeSpan.FromSeconds(90), provider.Metadata.Timeout);
    }

    [Fact]
    public void Codex_effective_timeout_is_propagated_via_metadata()
    {
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, TimeoutSeconds = 90 }, new FakeCodexRunner());
        Assert.Equal(TimeSpan.FromSeconds(90), provider.Metadata.Timeout);
    }

    [Fact]
    public void ClaudeCode_effective_timeout_is_propagated_via_metadata()
    {
        var provider = new ClaudeCodeEvidenceProvider(
            new ClaudeCodeOptions { Enabled = true, TimeoutSeconds = 300 }, new FakeClaudeCodeRunner());
        Assert.Equal(TimeSpan.FromSeconds(300), provider.Metadata.Timeout);
    }

    [Fact]
    public void Provider_without_own_timeout_reports_null_metadata_timeout()
    {
        var provider = new FakeTimeoutProvider("P", timeout: null, _ => Task.FromResult<IReadOnlyList<Evidence>>([Evidence("P")]));
        Assert.Null(provider.Metadata.Timeout);
    }

    // ── 3. Provider timeout still produces ErrorCategory=Timeout ─────────────

    [Fact]
    public async Task Provider_specific_timeout_produces_ErrorCategory_Timeout()
    {
        // Provider timeout 150ms is SMALLER than the 500ms global fallback — the
        // provider's own bound fires and is still reported as a Timeout, never
        // flattened to a generic failure.
        var provider = new FakeTimeoutProvider("P", TimeSpan.FromMilliseconds(150),
            async ct => { await Task.Delay(TimeSpan.FromMilliseconds(400), ct); return [Evidence("P")]; });

        var result = await Executor(TimeSpan.FromMilliseconds(500), provider).ExecuteAsync(Plan([provider], [Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal("Timeout", record.ErrorCategory);
        Assert.True(record.Duration < TimeSpan.FromMilliseconds(350),
            $"provider 150ms timeout must fire before the 500ms global cap (was {record.Duration})");

        var report = ProviderExecutionReport.FromRecords(result.Executions);
        Assert.Equal(1, report.TimeoutCount);
        Assert.Equal(1, report.Failures);
    }

    // ── 4. Run cancellation stays cancellation ───────────────────────────────

    [Fact]
    public async Task Run_cancellation_remains_cancellation_not_timeout()
    {
        var provider = new FakeTimeoutProvider("P", TimeSpan.FromSeconds(60),
            async ct => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return [Evidence("P")]; });

        using var cts = new CancellationTokenSource();
        var executor = Executor(TimeSpan.FromSeconds(60), provider);
        var task = executor.ExecuteAsync(Plan([provider], [Security]), Snapshot, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    // ── 5. Parallel steps keep independent timeout values ────────────────────

    [Fact]
    public async Task Parallel_steps_keep_independent_timeout_values()
    {
        // Both providers run concurrently (MaxConcurrency=2). P_short is cut at its
        // OWN 300ms; P_long's 2000ms budget lets it finish at ~700ms. A single shared
        // Council timer would have killed BOTH at 300ms.
        var maxActive = 0;
        var active = 0;
        var lockObj = new object();

        FakeTimeoutProvider WithCounter(string name, TimeSpan timeout, TimeSpan delay) => new(name, timeout,
            async ct =>
            {
                var n = Interlocked.Increment(ref active);
                lock (lockObj) { if (n > maxActive) maxActive = n; }
                try
                {
                    await Task.Delay(delay, ct);
                    return [Evidence(name)];
                }
                finally { Interlocked.Decrement(ref active); }
            });

        var shortProvider = WithCounter("P_short", TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(800));
        var longProvider = WithCounter("P_long", TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(700));

        var result = await Executor(TimeSpan.FromMilliseconds(500), maxConcurrency: 2, shortProvider, longProvider)
            .ExecuteAsync(Plan([shortProvider, longProvider], [Security]), Snapshot);

        Assert.Equal(2, maxActive); // both were in flight concurrently

        var shortRecord = Assert.Single(result.Executions, r => r.ProviderName == "P_short");
        Assert.False(shortRecord.Success);
        Assert.Equal("Timeout", shortRecord.ErrorCategory);

        var longRecord = Assert.Single(result.Executions, r => r.ProviderName == "P_long");
        Assert.True(longRecord.Success, "P_long's 2000ms timeout must NOT be cut by P_short's 300ms timeout");
    }

    // ── 6. No timeout configuration leaks into the external package ──────────

    [Fact]
    public async Task Provider_timeout_does_not_leak_into_external_package_contract()
    {
        var provider = new FakeTimeoutProvider("P", TimeSpan.FromSeconds(30),
            ct => Task.FromResult<IReadOnlyList<Evidence>>([Evidence("P")]));
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "P" });

        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);

        using var document = JsonDocument.Parse(packageJson);
        var records = document.RootElement.GetProperty("providerExecution").GetProperty("records");
        Assert.NotEmpty(records.EnumerateArray());
        foreach (var record in records.EnumerateArray())
            Assert.DoesNotContain(record.EnumerateObject(), p => p.Name is "timeout" or "timeoutSeconds");
    }

    // ── 7. Configuration binding for Evidence:Execution:ProviderTimeout ──────

    [Fact]
    public void ProviderTimeout_defaults_and_shipped_config_keys_are_bound()
    {
        // The global fallback default is preserved at both the core and DI surface.
        Assert.Equal(TimeSpan.FromSeconds(120), new EvidenceOptions().ProviderTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), new CouncilOptions().ProviderTimeout);

        // The shipped CLI + API configs carry the bound key (same convention as
        // Evidence:Execution:MaxConcurrency, M15.1).
        var cli = FindAppSettings("src", "EngineeringCouncil.Cli");
        var api = FindAppSettings("src", "EngineeringCouncil.Api");
        if (cli is not null) Assert.Equal(120, ExecutionTimeout(cli));
        if (api is not null) Assert.Equal(120, ExecutionTimeout(api));
    }

    private static int ExecutionTimeout(string appSettingsPath)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath), options);
        return doc.RootElement.GetProperty("Evidence").GetProperty("Execution").GetProperty("ProviderTimeout").GetInt32();
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

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-provider-timeout-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-provider-timeout-out-" + Guid.NewGuid().ToString("N"));

    private string Repo => _repo;

    private static FindingCategory Security => FindingCategory.Security;

    private static Evidence Evidence(string providerName) => new() { ProviderName = providerName, Success = true };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(TimeSpan globalTimeout, params IEvidenceProvider[] providers)
        => Executor(globalTimeout, maxConcurrency: 1, providers);

    private static EvidenceAcquisitionExecutor Executor(
        TimeSpan globalTimeout, int maxConcurrency, params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = globalTimeout, MaxConcurrency = maxConcurrency });

    private async Task<AnalysisPipeline> BuildPipelineAsync(IReadOnlyList<IEvidenceProvider> providers)
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "Sample.sln"), "solution\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "src", "Auth.cs"), "public class Auth { void Authorize() { } }\n");

        var options = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [Security],
            ProviderFailureMode = ProviderFailureMode.Continue,
            ProviderTimeout = TimeSpan.FromSeconds(60)
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new TestingAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    private sealed class FakeTimeoutProvider : IEvidenceProvider
    {
        private readonly EvidenceProviderMetadata _metadata;
        private readonly Func<CancellationToken, Task<IReadOnlyList<Evidence>>> _handler;

        public FakeTimeoutProvider(string name, TimeSpan? timeout, Func<CancellationToken, Task<IReadOnlyList<Evidence>>> handler)
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

        public EvidenceProviderMetadata Metadata => _metadata;
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => _handler(cancellationToken);
    }

    private sealed class FakeOpenCodeRunner : IOpenCodeProcessRunner
    {
        public Func<OpenCodeProcessRequest, CancellationToken, Task<OpenCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new OpenCodeProcessResult { ExitCode = 0, StandardOutput = "{}", StandardError = string.Empty });

        public Task<OpenCodeProcessResult> RunAsync(OpenCodeProcessRequest request, CancellationToken cancellationToken = default)
            => Handler(request, cancellationToken);
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        public Func<CodexProcessRequest, CancellationToken, Task<CodexProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new CodexProcessResult { ExitCode = 0, StandardOutput = "{}", StandardError = string.Empty });

        public Task<CodexProcessResult> RunAsync(CodexProcessRequest request, CancellationToken cancellationToken = default)
            => Handler(request, cancellationToken);
    }

    private sealed class FakeClaudeCodeRunner : IClaudeCodeProcessRunner
    {
        public Func<ClaudeCodeProcessRequest, CancellationToken, Task<ClaudeCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new ClaudeCodeProcessResult { ExitCode = 0, StandardOutput = "{}", StandardError = string.Empty });

        public Task<ClaudeCodeProcessResult> RunAsync(ClaudeCodeProcessRequest request, CancellationToken cancellationToken = default)
            => Handler(request, cancellationToken);
    }
}