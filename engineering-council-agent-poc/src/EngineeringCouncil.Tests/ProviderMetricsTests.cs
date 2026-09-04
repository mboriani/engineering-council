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
using EngineeringCouncil.Infrastructure.Llm;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using EngineeringCouncil.Tests.Contracts;
using EngineeringCouncil.Tests.Fakes;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 012.1 — real provider metrics. Confirms reliable, provider-neutral
/// per-execution and per-run metrics flow from the scripted Claude/OpenAI seam
/// through the executor and the run report, and that the diagnostic artifact
/// (provider-execution.json) carries them without ever leaking a secret. No
/// network, no credentials. Provider comparison/calibration is deliberately out
/// of scope: these tests assert structure and semantics, never quality.
/// </summary>
public sealed class ProviderMetricsTests : IDisposable
{
    private const string KeyVariable = "EC_TEST_METRICS_KEY";
    private const string KeyValue = "metrics-test-key-not-a-real-secret";

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-metrics-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-metrics-out-" + Guid.NewGuid().ToString("N"));

    private const string ValidJson = """
        {
          "schemaVersion": "1.0",
          "discipline": "Security",
          "observations": [
            { "type": "HardcodedSecret", "discipline": "Security", "title": "Hardcoded secret",
              "description": "A credential is embedded in source.", "severity": "High", "confidence": "Medium",
              "fileReferences": [ { "path": "src/Payments.cs", "startLine": 3 } ] }
          ]
        }
        """;

    public ProviderMetricsTests()
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Payments.cs"),
            "public class PaymentClient { const string K = \"x\"; }\n");
    }

    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        Files =
        [
            new ScannedFile { RelativePath = "src/Payments.cs", Extension = ".cs", SizeBytes = 1, LineCount = 5, Content = "public class PaymentClient { const string ApiKey = \"x\"; }" },
            new ScannedFile { RelativePath = "src/NotSelected.cs", Extension = ".cs", SizeBytes = 1, LineCount = 5, Content = "class NeverSent { }" },
        ]
    };

    private static EvidenceRequest Request()
    {
        var selected = Snapshot.Files[0];
        return new EvidenceRequest
        {
            RunId = "run1",
            RepositorySnapshot = Snapshot,
            Scope = EvidenceAcquisitionScope.Discipline,
            Discipline = FindingCategory.Security,
            Instructions = DisciplinePrompts.BuildInstructions(EvidenceAcquisitionScope.Discipline, FindingCategory.Security),
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "security-focused-v1", Files = [selected], TotalRepositoryFiles = 2,
                SelectedFileCount = 1, EstimatedContentSize = selected.Content!.Length
            },
            ProviderNames = ["Claude"],
            CorrelationId = "Claude:Security"
        };
    }

    private static ClaudeEvidenceProvider Claude(ScriptedLlmClient client, int maxRetries = 0, bool repair = false)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new ClaudeEvidenceProvider(client, new ClaudeProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "claude-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = repair
        }, new DisciplineEvidencePromptBuilder());
    }

    private static OpenAiEvidenceProvider OpenAi(ScriptedLlmClient client, int maxRetries = 0, bool repair = false)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new OpenAiEvidenceProvider(client, new OpenAiProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "openai-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = repair
        }, new DisciplineEvidencePromptBuilder());
    }

    private async Task<(IReadOnlyList<Evidence> Evidence, IReadOnlyList<ProviderExecutionRecord> Records)> ExecuteAsync(
        IEvidenceProvider provider, IReadOnlyCollection<FindingCategory> disciplines, ContextContentPolicy? policy = null)
    {
        var factory = new EvidenceProviderFactory([provider]);
        var executor = new EvidenceAcquisitionExecutor(
            factory, new RuleBasedAnalysisContextSelector(policy), new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });
        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, [provider], disciplines);
        var result = await executor.ExecuteAsync(plan, Snapshot);
        return (result.Evidence, result.Executions);
    }

    // ── Token mapping (Claude + OpenAI, provider-neutral) ─────────────────────

    [Fact]
    public async Task Claude_usage_is_mapped_to_typed_token_fields_on_evidence_and_record()
    {
        var client = new ScriptedLlmClient("claude").Returns(ValidJson, inputTokens: 1200, outputTokens: 300);
        var (evidence, records) = await ExecuteAsync(Claude(client), [FindingCategory.Security]);

        var ev = Assert.Single(evidence);
        Assert.Equal(1200, ev.InputTokens);
        Assert.Equal(300, ev.OutputTokens);
        Assert.Equal(1500, ev.TokensUsed);
        Assert.Equal("claude-test-model", ev.ProviderVersion);

        var record = Assert.Single(records);
        Assert.Equal(1200, record.InputTokens);
        Assert.Equal(300, record.OutputTokens);
        Assert.Equal(1500, record.TokensUsed);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task OpenAI_usage_is_mapped_to_typed_token_fields_on_evidence_and_record()
    {
        var client = new ScriptedLlmClient("openai").Returns(ValidJson, inputTokens: 90, outputTokens: 10);
        var (evidence, records) = await ExecuteAsync(OpenAi(client), [FindingCategory.Security]);

        var ev = Assert.Single(evidence);
        Assert.Equal(90, ev.InputTokens);
        Assert.Equal(10, ev.OutputTokens);
        Assert.Equal(100, ev.TokensUsed);
        Assert.Equal("openai-test-model", ev.ProviderVersion);

        var record = Assert.Single(records);
        Assert.Equal(90, record.InputTokens);
        Assert.Equal(10, record.OutputTokens);
        Assert.Equal(100, record.TokensUsed);
    }

    [Fact]
    public async Task Unknown_usage_stays_null_never_zero()
    {
        var client = new ScriptedLlmClient().Returns(ValidJson); // no usage reported
        var (evidence, records) = await ExecuteAsync(Claude(client), [FindingCategory.Security]);

        var ev = Assert.Single(evidence);
        Assert.Null(ev.InputTokens);
        Assert.Null(ev.OutputTokens);
        Assert.Null(ev.TokensUsed);

        var record = Assert.Single(records);
        Assert.Null(record.InputTokens);
        Assert.Null(record.OutputTokens);
        Assert.Null(record.TokensUsed);
    }

    [Fact]
    public async Task Total_tokens_sum_is_carried_when_both_sides_are_known()
    {
        var client = new ScriptedLlmClient().Returns(ValidJson, inputTokens: 400, outputTokens: 60);
        var (_, records) = await ExecuteAsync(Claude(client), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.Equal(400, record.InputTokens);
        Assert.Equal(60, record.OutputTokens);
        Assert.Equal(460, record.TokensUsed);
    }

    // ── Retry counting ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_first_try_success_records_zero_retries()
    {
        var (_, records) = await ExecuteAsync(Claude(new ScriptedLlmClient().Returns(ValidJson)), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.Equal(0, record.RetryCount);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task One_transient_failure_then_success_records_one_retry()
    {
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.RateLimit).Returns(ValidJson);
        var (evidence, records) = await ExecuteAsync(Claude(client, maxRetries: 2), [FindingCategory.Security]);

        Assert.Equal(2, client.CallCount);
        var ev = Assert.Single(evidence);
        Assert.Equal(1, ev.RetryCount);
        Assert.Equal("1", ev.Metadata["retryCount"]);
        var record = Assert.Single(records);
        Assert.Equal(1, record.RetryCount);
    }

    [Fact]
    public async Task Timeout_retries_are_counted_like_any_other_retry()
    {
        // Milestone 011.1 semantics: a provider timeout (Cancelled reported by the
        // transport, run token alive) is reclassified as Timeout and retried; those
        // retries must appear in RetryCount.
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.Cancelled).Fails(LlmErrorCategory.Cancelled).Returns(ValidJson);
        var (_, records) = await ExecuteAsync(Claude(client, maxRetries: 3), [FindingCategory.Security]);

        Assert.Equal(3, client.CallCount);
        var record = Assert.Single(records);
        Assert.Equal(2, record.RetryCount);
        Assert.True(record.Success);
    }

    // ── Structured repair (at most once) ──────────────────────────────────────

    [Fact]
    public async Task A_repair_success_records_attempt_and_success()
    {
        var client = new ScriptedLlmClient().Returns("oops, not json").Returns(ValidJson);
        var (evidence, records) = await ExecuteAsync(Claude(client, repair: true), [FindingCategory.Security]);

        var ev = Assert.Single(evidence);
        Assert.Equal(1, ev.RepairAttemptCount);
        Assert.True(ev.RepairSucceeded);

        var record = Assert.Single(records);
        Assert.Equal(1, record.RepairAttemptCount);
        Assert.True(record.RepairSucceeded);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task A_failed_repair_records_attempt_and_failure()
    {
        var client = new ScriptedLlmClient().Returns("still not json").Returns("also not json");
        var (_, records) = await ExecuteAsync(Claude(client, repair: true), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.False(record.Success);
        Assert.Equal(1, record.RepairAttemptCount);
        Assert.False(record.RepairSucceeded);
        Assert.Equal(LlmErrorCategory.SchemaValidation.ToString(), record.ErrorCategory);
    }

    [Fact]
    public async Task A_valid_first_response_records_no_repair()
    {
        var (_, records) = await ExecuteAsync(Claude(new ScriptedLlmClient().Returns(ValidJson), repair: true), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.Equal(0, record.RepairAttemptCount);
        Assert.Null(record.RepairSucceeded);
        Assert.True(record.Success);
    }

    // ── Model, duration, error category, finish reason, truncation ───────────

    [Fact]
    public async Task The_configured_model_appears_on_each_execution()
    {
        var (_, records) = await ExecuteAsync(Claude(new ScriptedLlmClient().Returns(ValidJson)), [FindingCategory.Security, FindingCategory.Reliability]);

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal("claude-test-model", r.ProviderVersion));
    }

    [Fact]
    public async Task Duration_is_positive_provider_wall_clock_time()
    {
        var (_, records) = await ExecuteAsync(Claude(new ScriptedLlmClient().Returns(ValidJson)), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.True(record.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task A_failed_execution_preserves_error_category_duration_and_retry_count()
    {
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.Authentication, "invalid api key");
        var (evidence, records) = await ExecuteAsync(Claude(client), [FindingCategory.Security]);

        var record = Assert.Single(records);
        Assert.False(record.Success);
        Assert.Equal(LlmErrorCategory.Authentication.ToString(), record.ErrorCategory);
        Assert.True(record.Duration > TimeSpan.Zero);
        Assert.Equal(0, record.RetryCount);            // auth is never retried
        Assert.False(string.IsNullOrWhiteSpace(record.ErrorMessage));
        Assert.DoesNotContain(KeyValue, record.ErrorMessage);

        var ev = Assert.Single(evidence);
        Assert.False(ev.Success);
        Assert.Equal(LlmErrorCategory.Authentication.ToString(), ev.ErrorCategory);
    }

    [Fact]
    public async Task Response_truncation_is_recorded_only_from_provider_metadata()
    {
        // A truncated flag from the provider is surfaced (here as a schema failure).
        var truncated = new ScriptedLlmClient("openai").Returns(ValidJson, finishReason: "max_tokens", truncated: true);
        var (_, truncatedRecords) = await ExecuteAsync(OpenAi(truncated), [FindingCategory.Security]);
        var truncatedRecord = Assert.Single(truncatedRecords);
        Assert.True(truncatedRecord.ResponseTruncated);
        Assert.False(truncatedRecord.Success);

        // A normal response is never flagged truncated — even a very short one.
        var normal = new ScriptedLlmClient().Returns(ValidJson);
        var (_, normalRecords) = await ExecuteAsync(Claude(normal), [FindingCategory.Security]);
        var normalRecord = Assert.Single(normalRecords);
        Assert.False(normalRecord.ResponseTruncated);
        Assert.True(normalRecord.Success);
    }

    [Fact]
    public async Task Finish_reason_is_captured_when_available_and_null_when_not()
    {
        var withFinish = new ScriptedLlmClient("openai").Returns(ValidJson, finishReason: "stop");
        var (_, withRecords) = await ExecuteAsync(OpenAi(withFinish), [FindingCategory.Security]);
        Assert.Equal("stop", Assert.Single(withRecords).FinishReason);

        var without = new ScriptedLlmClient().Returns(ValidJson);
        var (_, withoutRecords) = await ExecuteAsync(Claude(without), [FindingCategory.Security]);
        Assert.Null(Assert.Single(withoutRecords).FinishReason);
    }

    // ── Context reuse (Milestone 011.3 semantics, unchanged) ──────────────────

    [Fact]
    public async Task Context_metrics_reuse_the_m113_semantics_exactly()
    {
        // Repo has 2 files; the policy caps selection at 1. The record must report
        // the repository scope as "considered", the selection as the file count,
        // and the effective (policy-capped) characters.
        var policy = new ContextContentPolicy { MaxFiles = 1, MaxCharacters = 500_000, MaxCharactersPerFile = 8_000 };
        var (_, records) = await ExecuteAsync(Claude(new ScriptedLlmClient().Returns(ValidJson)), [FindingCategory.Security], policy);

        var record = Assert.Single(records);
        Assert.Equal(2, record.ContextFilesConsidered);   // repository scope
        Assert.Equal(1, record.ContextFileCount);         // selected files
        Assert.Equal(policy.EffectiveContextCharacters(Snapshot.Files[0]), record.ContextCharacterCount);
    }

    // ── Run-level aggregation (deterministic, known-token aware) ──────────────

    [Fact]
    public void Run_aggregation_is_deterministic_and_known_token_aware()
    {
        var records = new ProviderExecutionRecord[]
        {
            new() { ProviderName = "Claude", Success = true,  InputTokens = 1000, OutputTokens = 100, TokensUsed = 1100, RetryCount = 0, RepairAttemptCount = 0, Duration = TimeSpan.FromSeconds(1), ObservationsProduced = 2 },
            new() { ProviderName = "OpenAI", Success = true,  InputTokens = 500,  OutputTokens = 50,  TokensUsed = 550,  RetryCount = 1, RepairAttemptCount = 0, Duration = TimeSpan.FromSeconds(2), ObservationsProduced = 1 },
            new() { ProviderName = "OpenAI", Success = false, ErrorCategory = "Timeout", RetryCount = 2, RepairAttemptCount = 0, Duration = TimeSpan.FromSeconds(3), ErrorMessage = "timed out" },
            new() { ProviderName = "Claude", Success = false, ErrorCategory = "RateLimit", RetryCount = 1, RepairAttemptCount = 1, RepairSucceeded = true, Duration = TimeSpan.FromSeconds(4), ErrorMessage = "rate limited" },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Equal(4, report.TotalExecutions);
        Assert.Equal(2, report.SuccessfulExecutionCount);
        Assert.Equal(2, report.Failures);
        Assert.Equal(2, report.PartialProviderCount);        // both providers have success AND failure
        Assert.Equal(1500, report.TotalInputTokens);
        Assert.Equal(150, report.TotalOutputTokens);
        Assert.Equal(1650, report.TotalTokens);
        Assert.Equal(2, report.KnownTokenExecutionCount);    // only the two successful executions knew usage
        Assert.Equal(4, report.TotalRetries);                // 0 + 1 + 2 + 1
        Assert.Equal(1, report.TotalRepairAttempts);
        Assert.Equal(1, report.SuccessfulRepairs);
        Assert.Equal(1, report.TimeoutCount);
        Assert.Equal(3, report.ObservationsProduced);
        Assert.Equal(TimeSpan.FromSeconds(10), report.TotalDuration);

        var again = ProviderExecutionReport.FromRecords(records);
        Assert.Equal(report.TotalExecutions, again.TotalExecutions);
        Assert.Equal(report.TotalTokens, again.TotalTokens);
        Assert.Equal(report.PartialProviderCount, again.PartialProviderCount);
        Assert.Equal(report.TimeoutCount, again.TimeoutCount);
    }

    [Fact]
    public void Token_totals_are_null_when_no_execution_knows_usage()
    {
        var records = new ProviderExecutionRecord[]
        {
            new() { ProviderName = "Claude", Success = true, Duration = TimeSpan.FromSeconds(1) },
            new() { ProviderName = "OpenAI", Success = true, Duration = TimeSpan.FromSeconds(1) },
        };

        var report = ProviderExecutionReport.FromRecords(records);

        Assert.Null(report.TotalInputTokens);
        Assert.Null(report.TotalOutputTokens);
        Assert.Null(report.TotalTokens);
        Assert.Equal(0, report.KnownTokenExecutionCount);
    }

    // ── Diagnostic artifact: provider-execution.json ──────────────────────────

    [Fact]
    public async Task Provider_execution_json_carries_metrics_and_never_a_secret()
    {
        var client = new ScriptedLlmClient("claude").Returns(ValidJson, inputTokens: 1200, outputTokens: 300, finishReason: "end_turn");
        var pipeline = await BuildPipelineAsync([Claude(client)]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude" });

        var json = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "provider-execution.json"));
        Assert.Contains("\"inputTokens\"", json);
        Assert.Contains("\"outputTokens\"", json);
        Assert.Contains("\"retryCount\"", json);
        Assert.Contains("\"repairAttemptCount\"", json);
        Assert.Contains("\"finishReason\"", json);
        Assert.Contains("\"providerVersion\"", json);
        Assert.DoesNotContain(KeyValue, json);
        Assert.DoesNotContain(KeyVariable, json);

        // The package is untouched by detailed telemetry: still the 1.1 consumer contract.
        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.DoesNotContain(KeyValue, packageJson);
    }

    [Fact]
    public async Task Run_report_observations_aggregate_per_acquisition_step()
    {
        var client = new ScriptedLlmClient("claude")
            .Returns(ValidJson)
            .Returns(ValidJson);
        var pipeline = await BuildPipelineAsync([Claude(client)]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude" });

        var records = result.Run.ProviderExecution!.Records;
        Assert.Equal(2, records.Count);                       // Security + Reliability steps
        Assert.Equal(1, records.Count(r => r.ObservationsProduced > 0));
        Assert.Equal(result.Run.Observations.Count, result.Run.ProviderExecution.ObservationsProduced);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AnalysisPipeline> BuildPipelineAsync(IReadOnlyList<IEvidenceProvider> providers)
    {
        var evidenceOptions = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security, FindingCategory.Reliability],
            ProviderFailureMode = ProviderFailureMode.Continue
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), evidenceOptions),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new ReliabilityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            evidenceOptions,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
