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
/// Milestone 015.2D — token efficiency analysis. Provider-neutral DERIVED metrics
/// computed from already-captured authoritative telemetry: ContextTokenActivity,
/// FreshContextTokens, CacheReuseRatio. They are ACTIVITY accounting — NOT cost,
/// NOT billable tokens, NOT unique context, and they NEVER change the M12.1
/// TotalTokens semantics (TotalTokens = SumKnown(Input, Output)) or
/// KnownTokenExecutionCount. Unknown stays null (never 0); OpenCode/Codex remain
/// honestly unknown. All tests are offline (scripted fake runners / pure helpers).
/// </summary>
public sealed class TokenEfficiencyMetricsTests
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

    // ── Derived metric calculations (pure helpers) ───────────────────────────

    [Fact]
    public void ContextTokenActivity_sums_known_input_side_categories()
    {
        Assert.Equal(1_178_189, TokenEfficiencyMetrics.ContextTokenActivity(44, 52_193, 1_125_952));
        Assert.Equal(150, TokenEfficiencyMetrics.ContextTokenActivity(100, null, 50));   // partial known
        Assert.Equal(52_193, TokenEfficiencyMetrics.ContextTokenActivity(null, 52_193, null)); // cache-only
        Assert.Null(TokenEfficiencyMetrics.ContextTokenActivity(null, null, null));
    }

    [Fact]
    public void FreshContextTokens_sums_input_and_cache_creation()
    {
        Assert.Equal(52_237, TokenEfficiencyMetrics.FreshContextTokens(44, 52_193));
        Assert.Equal(100, TokenEfficiencyMetrics.FreshContextTokens(100, null));
        Assert.Equal(52_193, TokenEfficiencyMetrics.FreshContextTokens(null, 52_193));
        Assert.Null(TokenEfficiencyMetrics.FreshContextTokens(null, null));
    }

    [Fact]
    public void CacheReuseRatio_is_cache_read_over_activity()
    {
        Assert.Equal(0.5, TokenEfficiencyMetrics.CacheReuseRatio(1000, 2000));
        Assert.Null(TokenEfficiencyMetrics.CacheReuseRatio(null, 2000));     // cache-read unknown
        Assert.Null(TokenEfficiencyMetrics.CacheReuseRatio(1000, null));     // activity unknown
        Assert.Null(TokenEfficiencyMetrics.CacheReuseRatio(0, 0));           // no meaningful denominator
    }

    // ── The authoritative M15.2C fixture ─────────────────────────────────────

    [Fact]
    public void M15_2C_fixture_produces_the_expected_metrics()
    {
        const int input = 44;
        const int output = 14_325;
        const int cacheRead = 1_125_952;
        const int cacheCreation = 52_193;

        var activity = TokenEfficiencyMetrics.ContextTokenActivity(input, cacheCreation, cacheRead);
        var fresh = TokenEfficiencyMetrics.FreshContextTokens(input, cacheCreation);
        var ratio = TokenEfficiencyMetrics.CacheReuseRatio(cacheRead, activity);

        Assert.Equal(1_178_189, activity);                       // 44 + 52,193 + 1,125,952
        Assert.Equal(52_237, fresh);                             // 44 + 52,193
        Assert.Equal(0.9557, Math.Round(ratio!.Value, 4));       // 1,125,952 / 1,178,189 ≈ 0.95566
        Assert.Equal(14_369, input + output);                    // M12.1 total — cache NEVER added
        Assert.NotEqual(activity, input + output);
    }

    [Fact]
    public async Task M15_2C_fixture_flows_through_the_executor_unchanged()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(), 44, 14_325, 1_125_952, 52_193)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(44, record.InputTokens);
        Assert.Equal(14_325, record.OutputTokens);
        Assert.Equal(14_369, record.TokensUsed);                 // TotalTokens unchanged
        Assert.Equal(1_125_952, record.CacheReadInputTokens);
        Assert.Equal(52_193, record.CacheCreationInputTokens);
        Assert.Equal(1_178_189, record.ContextTokenActivity);
        Assert.Equal(52_237, record.FreshContextTokens);
        Assert.Equal(0.9557, Math.Round(record.CacheReuseRatio!.Value, 4));
    }

    // ── Null / unknown semantics ─────────────────────────────────────────────

    [Fact]
    public void Cache_values_absent_produce_appropriate_null_semantics()
    {
        // Input-only record: activity/fresh = input (input-side activity), ratio null.
        var record = new ProviderExecutionRecord
        {
            ProviderName = "ClaudeCode", Success = true,
            InputTokens = 179, OutputTokens = 17_605, TokensUsed = 17_784
        };
        Assert.Equal(179, TokenEfficiencyMetrics.ContextTokenActivity(record.InputTokens, record.CacheCreationInputTokens, record.CacheReadInputTokens));
        Assert.Equal(179, TokenEfficiencyMetrics.FreshContextTokens(record.InputTokens, record.CacheCreationInputTokens));
        Assert.Null(TokenEfficiencyMetrics.CacheReuseRatio(record.CacheReadInputTokens, 179));

        // No token data at all → everything null.
        var empty = new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true };
        Assert.Null(TokenEfficiencyMetrics.ContextTokenActivity(empty.InputTokens, empty.CacheCreationInputTokens, empty.CacheReadInputTokens));
        Assert.Null(TokenEfficiencyMetrics.FreshContextTokens(empty.InputTokens, empty.CacheCreationInputTokens));
        Assert.Null(TokenEfficiencyMetrics.CacheReuseRatio(empty.CacheReadInputTokens, null));
    }

    [Fact]
    public void Unknown_never_becomes_zero_in_run_level_aggregation()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },   // fully unknown
            new ProviderExecutionRecord { ProviderName = "Codex", Success = true },      // fully unknown
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Null(report.TotalContextTokenActivity);
        Assert.Null(report.TotalFreshContextTokens);
        Assert.Null(report.CacheReuseRatio);
        Assert.Null(report.TotalInputTokens);
        Assert.Null(report.TotalTokens);
        Assert.Equal(0, report.KnownTokenExecutionCount);
    }

    // ── TotalTokens / OpenCode / Codex semantics ─────────────────────────────

    [Fact]
    public void TotalTokens_semantics_remain_unchanged()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 44, OutputTokens = 14_325, TokensUsed = 14_369, CacheReadInputTokens = 1_125_952, CacheCreationInputTokens = 52_193 },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(14_369, report.TotalTokens);                 // input + output only
        Assert.Equal(44, report.TotalInputTokens);
        Assert.Equal(14_325, report.TotalOutputTokens);
        Assert.Equal(1_178_189, report.TotalContextTokenActivity); // cache NEVER folded in
        Assert.Equal(52_237, report.TotalFreshContextTokens);
        Assert.Equal(1, report.KnownTokenExecutionCount);
    }

    [Fact]
    public void OpenCode_Codex_unknown_data_produce_no_fabricated_derived_metrics()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },
            new ProviderExecutionRecord { ProviderName = "Codex", Success = true },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Null(report.TotalContextTokenActivity);
        Assert.Null(report.TotalFreshContextTokens);
        Assert.Null(report.CacheReuseRatio);
        Assert.All(records, r => Assert.Null(r.ContextTokenActivity));
        Assert.All(records, r => Assert.Null(r.FreshContextTokens));
        Assert.All(records, r => Assert.Null(r.CacheReuseRatio));
    }

    // ── Run-level aggregation ────────────────────────────────────────────────

    [Fact]
    public void Run_level_cache_reuse_ratio_uses_aggregate_totals_not_average()
    {
        var records = new[]
        {
            // ratio 0.5 (1000 / 2000)
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 100, CacheReadInputTokens = 1000, CacheCreationInputTokens = 900 },
            // ratio 0.7 (700 / 1000)
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 300, CacheReadInputTokens = 700 },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        // Average of ratios would be 0.6 — the aggregate is NOT that.
        Assert.NotEqual(0.6, report.CacheReuseRatio!.Value);
        Assert.Equal(0.5667, Math.Round(report.CacheReuseRatio.Value, 4));   // 1700 / 3000
        Assert.Equal(1700, report.TotalCacheReadInputTokens);
        Assert.Equal(3000, report.TotalContextTokenActivity);
    }

    [Fact]
    public void Run_level_aggregation_is_independent_of_record_order()
    {
        var a = new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 44, OutputTokens = 14_325, TokensUsed = 14_369, CacheReadInputTokens = 1_125_952, CacheCreationInputTokens = 52_193 };
        var b = new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true };

        var forward = ProviderExecutionReport.FromRecords([a, b]);
        var reverse = ProviderExecutionReport.FromRecords([b, a]);

        Assert.Equal(forward.TotalContextTokenActivity, reverse.TotalContextTokenActivity);
        Assert.Equal(forward.TotalFreshContextTokens, reverse.TotalFreshContextTokens);
        Assert.Equal(forward.CacheReuseRatio, reverse.CacheReuseRatio);
        Assert.Equal(forward.TotalCacheReadInputTokens, reverse.TotalCacheReadInputTokens);
        Assert.Equal(forward.TotalTokens, reverse.TotalTokens);
    }

    // ── Artifacts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provider_execution_json_serializes_derived_metrics()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(), 44, 14_325, 1_125_952, 52_193)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var json = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "provider-execution.json"));
        Assert.Contains("\"contextTokenActivity\": 1178189", json);
        Assert.Contains("\"freshContextTokens\": 52237", json);
        Assert.Contains("\"cacheReuseRatio\": 0.95566", json);
        Assert.Contains("\"totalContextTokenActivity\": 1178189", json);
        Assert.Contains("\"totalFreshContextTokens\": 52237", json);
        Assert.Contains("\"totalTokens\": 14369", json);
        Assert.Contains("\"knownTokenExecutionCount\": 1", json);
    }

    [Fact]
    public async Task Engineering_review_package_gains_no_derived_or_cache_fields()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(), 44, 14_325, 1_125_952, 52_193)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);

        // Historical M12.1 token fields remain inside the existing providerExecution section.
        Assert.Contains("\"inputTokens\": 44", packageJson);
        Assert.Contains("\"totalTokens\": 14369", packageJson);

        // Cache (M15.2C) AND derived (M15.2D) telemetry must NOT leak into the external package.
        foreach (var leaked in new[]
        {
            "cacheReadInputTokens", "cacheCreationInputTokens", "totalCacheReadInputTokens", "totalCacheCreationInputTokens",
            "contextTokenActivity", "freshContextTokens", "cacheReuseRatio", "totalContextTokenActivity", "totalFreshContextTokens"
        })
            Assert.DoesNotContain(leaked, packageJson, StringComparison.Ordinal);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-token-eff-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-token-eff-out-" + Guid.NewGuid().ToString("N"));

    private string Repo => _repo;

    private ClaudeCodeOptions EnabledOptions() => new() { Enabled = true, Executable = "claude" };

    private static ClaudeCodeProcessResult Result(int exitCode, string stdout, string stderr = "")
        => new() { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    private static string Envelope(string resultText, int? inputTokens, int? outputTokens, int? cacheRead, int? cacheCreation)
        => $@"{{""type"":""result"",""subtype"":""success"",""is_error"":false,""result"":{JsonSerializer.Serialize(resultText)},""usage"":{{""input_tokens"":{(inputTokens?.ToString() ?? "null")},""output_tokens"":{(outputTokens?.ToString() ?? "null")},""cache_read_input_tokens"":{(cacheRead?.ToString() ?? "null")},""cache_creation_input_tokens"":{(cacheCreation?.ToString() ?? "null")}}}}}";

    private static string StructuredJson()
        => """
            {
              "schemaVersion": "1.0",
              "discipline": "Security",
              "observations": [
                {
                  "type": "HardcodedSecret",
                  "discipline": "Security",
                  "title": "Possible sensitive value in source/config",
                  "description": "The agent found a likely credential while exploring.",
                  "severity": "Medium",
                  "confidence": "Medium",
                  "ruleId": "AT-001",
                  "fileReferences": [ { "path": "src/Auth.cs" } ],
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
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

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
        public Func<ClaudeCodeProcessRequest, CancellationToken, Task<ClaudeCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(), null, null, null, null)));

        public Task<ClaudeCodeProcessResult> RunAsync(ClaudeCodeProcessRequest request, CancellationToken cancellationToken = default)
            => Handler(request, cancellationToken);
    }
}