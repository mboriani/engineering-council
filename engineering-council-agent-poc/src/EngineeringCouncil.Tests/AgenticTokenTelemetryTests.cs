using System.Text.Json;
using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Acquisition;
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
/// Milestone 015.2A — agentic token usage telemetry. Measurement ONLY: capture
/// per-execution token usage whenever the runtime reports authoritative usage in
/// the machine-readable output the adapters ALREADY consume, reusing the M12.1
/// provider-neutral telemetry surface (<see cref="Evidence.InputTokens"/>,
/// <see cref="Evidence.OutputTokens"/>, <see cref="Evidence.TokensUsed"/> and the
/// run report's known-token aggregation). Unavailable usage stays null (never 0);
/// nothing is estimated; no runtime output is invented to force a number.
///
/// Reality inspected per runtime:
/// - Claude Code: <c>--output-format json</c> result envelope carries an
///   authoritative <c>usage</c> object (<c>input_tokens</c>, <c>output_tokens</c>,
///   cache categories, <c>total_cost_usd</c>) — mapped.
/// - OpenCode: the default <c>opencode run</c> stdout captured by the adapter is the
///   final assistant text only; no machine-readable usage → null.
/// - Codex: the default <c>codex exec</c> formatted stdout captured by the adapter is
///   the final message only; no machine-readable usage → null.
///
/// All tests are offline and use scripted fake runners — no network, no real agent
/// execution, no credentials, no paid calls.
/// </summary>
public sealed class AgenticTokenTelemetryTests
{
    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        ProjectFiles = ["src/App.csproj"],
        Files =
        [
            new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 40, Content = "public class Auth { void Authorize(){} } // Authentication ApiKey" },
            new ScannedFile { RelativePath = "tests/AuthTests.cs", Extension = ".cs", SizeBytes = 1, LineCount = 20, Content = "[Fact] void T(){ Assert.True(true); } // xunit" },
            new ScannedFile { RelativePath = "README.md", Extension = ".md", SizeBytes = 1, LineCount = 10, Content = "# Repo" },
        ]
    };

    // ── Claude Code: authoritative usage from the result envelope ────────────

    [Fact]
    public async Task ClaudeCode_reported_usage_maps_to_evidence_and_record()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"), inputTokens: 1200, outputTokens: 300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(1200, evidence.InputTokens);
        Assert.Equal(300, evidence.OutputTokens);
        Assert.Equal(1500, evidence.TokensUsed);
        Assert.Equal("1200", evidence.Metadata["inputTokens"]);
        Assert.Equal("300", evidence.Metadata["outputTokens"]);
        Assert.Equal("1500", evidence.Metadata["totalTokens"]);

        var record = Assert.Single(result.Executions);
        Assert.Equal(1200, record.InputTokens);
        Assert.Equal(300, record.OutputTokens);
        Assert.Equal(1500, record.TokensUsed);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task ClaudeCode_envelope_without_usage_keeps_tokens_null()
    {
        // A valid result envelope with NO `usage` object (older/leaner CLI output)
        // must not fabricate numbers.
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"))))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
        Assert.Null(evidence.InputTokens);
        Assert.Null(evidence.OutputTokens);
        Assert.Null(evidence.TokensUsed);

        var record = Assert.Single(result.Executions);
        Assert.Null(record.InputTokens);
        Assert.Null(record.OutputTokens);
        Assert.Null(record.TokensUsed);
    }

    [Fact]
    public async Task ClaudeCode_total_tokens_computed_only_when_usage_is_valid()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"), inputTokens: 400, outputTokens: 60)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(400, record.InputTokens);
        Assert.Equal(60, record.OutputTokens);
        Assert.Equal(460, record.TokensUsed);            // authoritative input + output
        Assert.Equal(record.InputTokens + record.OutputTokens, record.TokensUsed);

        var unknown = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"))))
        };
        var unknownProvider = new ClaudeCodeEvidenceProvider(EnabledOptions(), unknown);
        var unknownResult = await Executor(unknownProvider).ExecuteAsync(Plan([unknownProvider], [FindingCategory.Security]), Snapshot);
        Assert.Null(Assert.Single(unknownResult.Executions).TokensUsed);
    }

    // ── OpenCode / Codex: no machine-readable usage in the captured output ───

    [Fact]
    public async Task OpenCode_captured_output_exposes_no_usage_so_tokens_stay_null()
    {
        // The default `opencode run` stdout consumed by the adapter is the final
        // assistant text (the structured observations envelope) — no token usage.
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(EnabledOpenCodeOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        Assert.True(Assert.Single(result.Evidence).Success);
        var record = Assert.Single(result.Executions);
        Assert.True(record.Success);
        Assert.Null(record.InputTokens);
        Assert.Null(record.OutputTokens);
        Assert.Null(record.TokensUsed);
    }

    [Fact]
    public async Task Codex_captured_output_exposes_no_usage_so_tokens_stay_null()
    {
        // The default `codex exec` formatted stdout consumed by the adapter is the
        // final message (the structured observations envelope) — no token usage.
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledCodexOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        Assert.True(Assert.Single(result.Evidence).Success);
        var record = Assert.Single(result.Executions);
        Assert.True(record.Success);
        Assert.Null(record.InputTokens);
        Assert.Null(record.OutputTokens);
        Assert.Null(record.TokensUsed);
    }

    [Fact]
    public async Task Unknown_agentic_usage_is_null_never_zero()
    {
        // Three-agent run with no runtime reporting usage: the run report must keep
        // every aggregate null — unknown must never become 0.
        var openCode = new OpenCodeEvidenceProvider(EnabledOpenCodeOptions(), new FakeOpenCodeRunner());
        var codex = new CodexEvidenceProvider(EnabledCodexOptions(), new FakeCodexRunner());
        var claudeCode = new ClaudeCodeEvidenceProvider(EnabledOptions(), new FakeClaudeCodeRunner());

        var result = await Executor(openCode, codex, claudeCode)
            .ExecuteAsync(Plan([openCode, codex, claudeCode], [FindingCategory.Security]), Snapshot);

        var report = ProviderExecutionReport.FromRecords(result.Executions);
        Assert.Equal(3, report.TotalExecutions);
        Assert.Equal(3, report.SuccessfulExecutionCount);
        Assert.Null(report.TotalInputTokens);
        Assert.Null(report.TotalOutputTokens);
        Assert.Null(report.TotalTokens);
        Assert.Equal(0, report.KnownTokenExecutionCount);
        Assert.All(result.Executions, r => Assert.Null(r.TokensUsed));
    }

    // ── Evidence → ProviderExecution flow and run aggregation ────────────────

    [Fact]
    public async Task Agentic_token_telemetry_survives_the_evidence_to_execution_flow()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"), inputTokens: 1200, outputTokens: 300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        Assert.NotNull(result.Run.ProviderExecution);
        var record = Assert.Single(result.Run.ProviderExecution!.Records);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
        Assert.Equal(1200, record.InputTokens);
        Assert.Equal(300, record.OutputTokens);
        Assert.Equal(1500, record.TokensUsed);
        Assert.Equal(1200, result.Run.ProviderExecution.TotalInputTokens);
        Assert.Equal(300, result.Run.ProviderExecution.TotalOutputTokens);
        Assert.Equal(1500, result.Run.ProviderExecution.TotalTokens);
        Assert.Equal(1, result.Run.ProviderExecution.KnownTokenExecutionCount);
    }

    [Fact]
    public void Mixed_known_unknown_run_aggregation_sums_only_known()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 1200, OutputTokens = 300, TokensUsed = 1500 },
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 400, OutputTokens = 60, TokensUsed = 460 },
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },            // unknown
            new ProviderExecutionRecord { ProviderName = "Codex", Success = true },               // unknown
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(4, report.TotalExecutions);
        Assert.Equal(1600, report.TotalInputTokens);
        Assert.Equal(360, report.TotalOutputTokens);
        Assert.Equal(1960, report.TotalTokens);
        Assert.Equal(2, report.KnownTokenExecutionCount);
    }

    [Fact]
    public void KnownTokenExecutionCount_counts_only_executions_with_known_usage()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 100, OutputTokens = 10, TokensUsed = 110 },
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },            // unknown → never counted
            new ProviderExecutionRecord { ProviderName = "Codex", Success = true },               // unknown → never counted
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = false, ErrorCategory = "Timeout" }, // unknown → never counted
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(1, report.KnownTokenExecutionCount);
        Assert.Equal(110, report.TotalTokens);
        Assert.Equal(100, report.TotalInputTokens);
        Assert.Equal(10, report.TotalOutputTokens);
    }

    // ── Artifacts: telemetry, contract, no secrets ───────────────────────────

    [Fact]
    public async Task Provider_execution_json_serializes_agentic_token_fields()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"), inputTokens: 1200, outputTokens: 300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var json = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "provider-execution.json"));
        Assert.Contains("\"inputTokens\": 1200", json);
        Assert.Contains("\"outputTokens\": 300", json);
        Assert.Contains("\"tokensUsed\": 1500", json);
        Assert.Contains("\"knownTokenExecutionCount\": 1", json);
        Assert.Contains("\"totalInputTokens\": 1200", json);
        Assert.Contains("\"totalOutputTokens\": 300", json);
        Assert.Contains("\"totalTokens\": 1500", json);
    }

    [Fact]
    public async Task Package_contract_stays_1_1_with_agentic_token_telemetry()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"), inputTokens: 1200, outputTokens: 300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        // Token usage is operational telemetry: it appears ONLY inside the pre-existing
        // `providerExecution` section (M12.1 contract), never as a new package field and
        // never inside the product-level findings.
        using var document = JsonDocument.Parse(packageJson);
        var root = document.RootElement;
        Assert.Contains("providerExecution", root.EnumerateObject().Select(p => p.Name));
        Assert.Contains("totalTokens", root.GetProperty("providerExecution").EnumerateObject().Select(p => p.Name));
        Assert.Equal(1500, root.GetProperty("providerExecution").GetProperty("totalTokens").GetInt32());
        Assert.Equal(1, root.GetProperty("providerExecution").GetProperty("knownTokenExecutionCount").GetInt32());
        Assert.DoesNotContain("inputTokens", root.GetProperty("findings").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("ClaudeCode", packageJson);
    }

    [Fact]
    public void No_credential_or_secret_appears_in_serialized_telemetry()
    {
        var evidence = new Evidence
        {
            ProviderName = "ClaudeCode",
            ProviderId = "claudecode",
            ProviderType = EvidenceProviderType.Agentic,
            Success = true,
            InputTokens = 1200,
            OutputTokens = 300,
            TokensUsed = 1500,
            Metadata = new Dictionary<string, string>
            {
                ["provider"] = "ClaudeCode",
                ["agentRuntime"] = "claude-code",
                ["inputTokens"] = "1200",
                ["outputTokens"] = "300",
                ["totalTokens"] = "1500"
            }
        };

        var serialized = JsonSerializer.Serialize(evidence, CouncilJson.Options);
        Assert.DoesNotContain("sk-", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"inputTokens\": 1200", serialized);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-agentic-tokens-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-agentic-tokens-out-" + Guid.NewGuid().ToString("N"));

    private string Repo => _repo;

    private ClaudeCodeOptions EnabledOptions() => new() { Enabled = true, Executable = "claude" };
    private OpenCodeOptions EnabledOpenCodeOptions() => new() { Enabled = true, Executable = "opencode" };
    private CodexOptions EnabledCodexOptions() => new() { Enabled = true, Executable = "codex" };

    private static ClaudeCodeProcessResult Result(int exitCode, string stdout, string stderr = "")
        => new() { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    /// <summary>The Claude Code CLI's own result envelope for <c>--output-format json</c>.</summary>
    private static string Envelope(string resultText, int? inputTokens = null, int? outputTokens = null)
    {
        var usage = inputTokens.HasValue || outputTokens.HasValue
            ? $@",""usage"":{{""input_tokens"":{(inputTokens?.ToString() ?? "null")},""output_tokens"":{(outputTokens?.ToString() ?? "null")}}}"
            : string.Empty;
        return $@"{{""type"":""result"",""subtype"":""success"",""is_error"":false,""result"":{JsonSerializer.Serialize(resultText)}{usage}}}";
    }

    private static string StructuredJson(FindingCategory discipline, string filePath)
        => $$"""
            {
              "schemaVersion": "1.0",
              "discipline": "{{discipline}}",
              "observations": [
                {
                  "type": "HardcodedSecret",
                  "discipline": "{{discipline}}",
                  "title": "Possible sensitive value in source/config",
                  "description": "The agent found a likely credential while exploring.",
                  "severity": "Medium",
                  "confidence": "Medium",
                  "ruleId": "AT-001",
                  "fileReferences": [ { "path": "{{filePath}}" } ],
                  "symbolReferences": [],
                  "lineReferences": [],
                  "evidenceExcerpt": "Agent exploration.",
                  "recommendationHint": "Review and rotate.",
                  "tags": ["agentic"]
                }
              ]
            }
            """;

    private static AnalysisRunConfiguration Config() => new() { RunId = "run1" };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(params IEvidenceProvider[] providers)
        => Executor(TimeSpan.FromSeconds(30), providers);

    private static EvidenceAcquisitionExecutor Executor(TimeSpan timeout, params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = timeout });

    private async Task<AnalysisPipeline> BuildPipelineAsync(IReadOnlyList<IEvidenceProvider> providers)
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "Sample.sln"), "solution\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "src", "Auth.cs"), "public class Auth { void Authorize() { } }\n");

        var options = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security],
            ProviderFailureMode = ProviderFailureMode.Continue
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

    private sealed class FakeClaudeCodeRunner : IClaudeCodeProcessRunner
    {
        public ClaudeCodeProcessRequest? LastRequest { get; private set; }

        public Func<ClaudeCodeProcessRequest, CancellationToken, Task<ClaudeCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(FindingCategory.Security, "src/Auth.cs"))));

        public Task<ClaudeCodeProcessResult> RunAsync(ClaudeCodeProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }

    private sealed class FakeOpenCodeRunner : IOpenCodeProcessRunner
    {
        public OpenCodeProcessRequest? LastRequest { get; private set; }

        public Func<OpenCodeProcessRequest, CancellationToken, Task<OpenCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new OpenCodeProcessResult { ExitCode = 0, StandardOutput = StructuredJson(FindingCategory.Security, "src/Auth.cs"), StandardError = string.Empty });

        public Task<OpenCodeProcessResult> RunAsync(OpenCodeProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        public CodexProcessRequest? LastRequest { get; private set; }

        public Func<CodexProcessRequest, CancellationToken, Task<CodexProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new CodexProcessResult { ExitCode = 0, StandardOutput = StructuredJson(FindingCategory.Security, "src/Auth.cs"), StandardError = string.Empty });

        public Task<CodexProcessResult> RunAsync(CodexProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }
}