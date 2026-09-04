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
/// Milestone 015.2C — Claude Code cache-token telemetry. The Claude Code
/// <c>--output-format json</c> result envelope also carries authoritative
/// <c>usage.cache_read_input_tokens</c> / <c>usage.cache_creation_input_tokens</c>;
/// those are now preserved as SEPARATE, provider-neutral operational telemetry
/// (<see cref="Evidence.CacheReadInputTokens"/> / <see cref="Evidence.CacheCreationInputTokens"/>
/// and the matching <see cref="ProviderExecutionRecord"/> fields + run-level
/// SumKnown totals). The M12.1 <c>TotalTokens = InputTokens + OutputTokens</c>
/// semantics are UNCHANGED — cache tokens are never added into them.
///
/// Missing values stay null (never 0); OpenCode/Codex remain honestly unknown.
/// All tests are offline with scripted fake runners — no network, no Claude
/// runtime, no credentials, no paid calls.
/// </summary>
public sealed class ClaudeCodeCacheTokenTelemetryTests
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

    // ── Envelope → extraction mapping ─────────────────────────────────────────

    [Fact]
    public async Task ClaudeCode_cache_read_input_tokens_maps_to_evidence_metadata_and_record()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(47377, evidence.CacheReadInputTokens);

        var record = Assert.Single(result.Executions);
        Assert.Equal(47377, record.CacheReadInputTokens);
        Assert.Equal("47377", evidence.Metadata["cacheReadInputTokens"]);
    }

    [Fact]
    public async Task ClaudeCode_cache_creation_input_tokens_maps_to_evidence_metadata_and_record()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(16300, evidence.CacheCreationInputTokens);

        var record = Assert.Single(result.Executions);
        Assert.Equal(16300, record.CacheCreationInputTokens);
        Assert.Equal("16300", evidence.Metadata["cacheCreationInputTokens"]);
    }

    [Fact]
    public async Task Both_cache_values_propagate_to_evidence()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(47377, evidence.CacheReadInputTokens);
        Assert.Equal(16300, evidence.CacheCreationInputTokens);
        Assert.Equal(179, evidence.InputTokens);
        Assert.Equal(17605, evidence.OutputTokens);
    }

    [Fact]
    public async Task Both_cache_values_propagate_to_provider_execution_record()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(47377, record.CacheReadInputTokens);
        Assert.Equal(16300, record.CacheCreationInputTokens);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task Missing_cache_values_remain_null()
    {
        // Envelope WITHOUT the cache keys (older/leaner usage object): nothing is fabricated.
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson(), inputTokens: 179, outputTokens: 17605)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Null(evidence.CacheReadInputTokens);
        Assert.Null(evidence.CacheCreationInputTokens);
        Assert.False(evidence.Metadata.ContainsKey("cacheReadInputTokens"));
        Assert.False(evidence.Metadata.ContainsKey("cacheCreationInputTokens"));

        var record = Assert.Single(result.Executions);
        Assert.Null(record.CacheReadInputTokens);
        Assert.Null(record.CacheCreationInputTokens);
    }

    [Fact]
    public async Task Missing_cache_values_never_become_zero()
    {
        // Explicit null in the envelope must stay null — never 0.
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: null, cacheCreation: null)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Null(evidence.CacheReadInputTokens);
        Assert.Null(evidence.CacheCreationInputTokens);
        Assert.False(evidence.Metadata.ContainsKey("cacheReadInputTokens"));
        Assert.False(evidence.Metadata.ContainsKey("cacheCreationInputTokens"));

        var record = Assert.Single(result.Executions);
        Assert.Null(record.CacheReadInputTokens);
        Assert.Null(record.CacheCreationInputTokens);
    }

    [Fact]
    public async Task TotalTokens_still_excludes_cache_values()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(17784, record.TokensUsed);              // 179 + 17605 — cache excluded
        Assert.Equal(179 + 17605, record.TokensUsed);
        Assert.NotEqual(179 + 17605 + 47377 + 16300, record.TokensUsed);
    }

    [Fact]
    public async Task Existing_input_output_behavior_is_unchanged()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(179, evidence.InputTokens);
        Assert.Equal(17605, evidence.OutputTokens);
        Assert.Equal(17784, evidence.TokensUsed);
        Assert.Equal("179", evidence.Metadata["inputTokens"]);
        Assert.Equal("17605", evidence.Metadata["outputTokens"]);
        Assert.Equal("17784", evidence.Metadata["totalTokens"]);

        var record = Assert.Single(result.Executions);
        Assert.Equal(179, record.InputTokens);
        Assert.Equal(17605, record.OutputTokens);
        Assert.Equal(17784, record.TokensUsed);
    }

    // ── Artifacts: provider-execution.json, package, run aggregation ──────────

    [Fact]
    public async Task Provider_execution_json_serializes_cache_telemetry()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var json = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "provider-execution.json"));
        Assert.Contains("\"cacheReadInputTokens\": 47377", json);
        Assert.Contains("\"cacheCreationInputTokens\": 16300", json);
        Assert.Contains("\"totalCacheReadInputTokens\": 47377", json);
        Assert.Contains("\"totalCacheCreationInputTokens\": 16300", json);
        Assert.Contains("\"tokensUsed\": 17784", json);
        Assert.Contains("\"totalTokens\": 17784", json);
        Assert.Contains("\"knownTokenExecutionCount\": 1", json);
    }

    [Fact]
    public async Task Engineering_review_package_does_not_gain_cache_token_fields()
    {
        var fake = new FakeClaudeCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, EnvelopeWithCache(StructuredJson(), 179, 17605, cacheRead: 47377, cacheCreation: 16300)))
        };
        var provider = new ClaudeCodeEvidenceProvider(EnabledOptions(), fake);
        var pipeline = await BuildPipelineAsync([provider]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = Repo, ProviderName = "ClaudeCode" });

        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);

        using var document = JsonDocument.Parse(packageJson);
        var root = document.RootElement;
        var providerExecution = root.GetProperty("providerExecution");
        // Historical M12.1 token telemetry stays inside the existing section.
        Assert.Contains("totalInputTokens", providerExecution.EnumerateObject().Select(p => p.Name));
        Assert.Contains("totalTokens", providerExecution.EnumerateObject().Select(p => p.Name));
        var record = providerExecution.GetProperty("records").EnumerateArray().Single();
        Assert.Contains("inputTokens", record.EnumerateObject().Select(p => p.Name));
        // Cache telemetry must NOT leak into the external package (run level or records).
        foreach (var leaked in new[]
        {
            "cacheReadInputTokens", "cacheCreationInputTokens",
            "totalCacheReadInputTokens", "totalCacheCreationInputTokens"
        })
        {
            Assert.DoesNotContain(providerExecution.EnumerateObject().Select(p => p.Name), n => n == leaked);
            Assert.DoesNotContain(record.EnumerateObject().Select(p => p.Name), n => n == leaked);
        }
        Assert.DoesNotContain("cacheReadInputTokens", packageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("cacheCreationInputTokens", packageJson, StringComparison.Ordinal);
        Assert.Contains("\"inputTokens\": 179", packageJson);
        Assert.Contains("\"totalTokens\": 17784", packageJson);
    }

    [Fact]
    public void Run_level_cache_totals_use_sum_known_semantics()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 100, OutputTokens = 10, TokensUsed = 110, CacheReadInputTokens = 47377, CacheCreationInputTokens = 16300 },
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, InputTokens = 200, OutputTokens = 20, TokensUsed = 220, CacheReadInputTokens = 1000, CacheCreationInputTokens = 500 },
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },                        // no usage at all
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(48377, report.TotalCacheReadInputTokens);        // 47377 + 1000
        Assert.Equal(16800, report.TotalCacheCreationInputTokens);    // 16300 + 500
        Assert.Equal(300, report.TotalInputTokens);
        Assert.Equal(30, report.TotalOutputTokens);
        Assert.Equal(330, report.TotalTokens);
        Assert.Equal(2, report.KnownTokenExecutionCount);             // input/output known, cache-independent
    }

    [Fact]
    public void Run_level_cache_totals_are_null_and_never_alter_known_token_count_when_only_cache_is_known()
    {
        var records = new[]
        {
            new ProviderExecutionRecord { ProviderName = "ClaudeCode", Success = true, CacheReadInputTokens = 47377, CacheCreationInputTokens = 16300 },
            new ProviderExecutionRecord { ProviderName = "OpenCode", Success = true },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(47377, report.TotalCacheReadInputTokens);
        Assert.Equal(16300, report.TotalCacheCreationInputTokens);
        Assert.Null(report.TotalInputTokens);
        Assert.Null(report.TotalOutputTokens);
        Assert.Null(report.TotalTokens);
        Assert.Equal(0, report.KnownTokenExecutionCount);             // cache alone is NOT "known" M12.1 usage
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-cache-tokens-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-cache-tokens-out-" + Guid.NewGuid().ToString("N"));

    private string Repo => _repo;

    private ClaudeCodeOptions EnabledOptions() => new() { Enabled = true, Executable = "claude" };

    private static ClaudeCodeProcessResult Result(int exitCode, string stdout, string stderr = "")
        => new() { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    /// <summary>Result envelope WITHOUT cache keys — the "missing" path.</summary>
    private static string Envelope(string resultText, int? inputTokens = null, int? outputTokens = null)
    {
        var usage = inputTokens.HasValue || outputTokens.HasValue
            ? $@",""usage"":{{""input_tokens"":{(inputTokens?.ToString() ?? "null")},""output_tokens"":{(outputTokens?.ToString() ?? "null")}}}"
            : string.Empty;
        return $@"{{""type"":""result"",""subtype"":""success"",""is_error"":false,""result"":{JsonSerializer.Serialize(resultText)}{usage}}}";
    }

    /// <summary>Result envelope WITH all four usage keys present (nullable cache = explicit-null path).</summary>
    private static string EnvelopeWithCache(string resultText, int? inputTokens, int? outputTokens, int? cacheRead, int? cacheCreation)
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
            = (_, _) => Task.FromResult(Result(0, Envelope(StructuredJson())));

        public Task<ClaudeCodeProcessResult> RunAsync(ClaudeCodeProcessRequest request, CancellationToken cancellationToken = default)
            => Handler(request, cancellationToken);
    }
}