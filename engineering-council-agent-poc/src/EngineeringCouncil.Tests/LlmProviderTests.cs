using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Llm;
using EngineeringCouncil.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 011 — real LLM evidence providers. Every test here runs through the
/// scripted client seam: no network, no credentials, no paid calls. Covers
/// configuration, planning, prompt construction, structured-response validation,
/// execution (retry/timeout/cancellation), and telemetry.
/// </summary>
public sealed class LlmProviderTests
{
    private const string TestKeyVariable = "EC_TEST_LLM_KEY";
    private const string TestKeyValue = "test-key-value-not-a-real-secret";

    private const string ValidSecurityJson = """
        {
          "schemaVersion": "1.0",
          "discipline": "Security",
          "observations": [
            { "type": "HardcodedSecret", "discipline": "Security", "title": "Hardcoded secret",
              "description": "A credential is embedded in source.", "severity": "High", "confidence": "Medium",
              "fileReferences": [ { "path": "src/A.cs", "startLine": 3 } ] }
          ]
        }
        """;

    private static ClaudeProviderOptions ClaudeOptions(int maxRetries = 0, bool repair = false)
    {
        Environment.SetEnvironmentVariable(TestKeyVariable, TestKeyValue);
        return new ClaudeProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = TestKeyVariable, Model = "claude-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = repair
        };
    }

    private static OpenAiProviderOptions OpenAiOptions(int maxRetries = 0, bool repair = false)
    {
        Environment.SetEnvironmentVariable(TestKeyVariable, TestKeyValue);
        return new OpenAiProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = TestKeyVariable, Model = "openai-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = repair
        };
    }

    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        Files =
        [
            new ScannedFile { RelativePath = "src/A.cs", Extension = ".cs", SizeBytes = 1, LineCount = 5, Content = "class A { const string K = \"x\"; }" },
            new ScannedFile { RelativePath = "src/NotSelected.cs", Extension = ".cs", SizeBytes = 1, LineCount = 5, Content = "class NeverSent { }" },
        ]
    };

    private static EvidenceRequest Request(FindingCategory discipline = FindingCategory.Security)
    {
        var selected = Snapshot.Files[0];   // only the FIRST file is selected context
        return new EvidenceRequest
        {
            RunId = "run1",
            RepositorySnapshot = Snapshot,
            Scope = EvidenceAcquisitionScope.Discipline,
            Discipline = discipline,
            Instructions = DisciplinePrompts.BuildInstructions(EvidenceAcquisitionScope.Discipline, discipline),
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "security-focused-v1", Files = [selected], TotalRepositoryFiles = 2,
                SelectedFileCount = 1, EstimatedContentSize = selected.Content!.Length
            },
            ProviderNames = ["Claude"],
            CorrelationId = "Claude:Security"
        };
    }

    private static ClaudeEvidenceProvider Claude(ScriptedLlmClient client, ClaudeProviderOptions? options = null,
        ILogger<ClaudeEvidenceProvider>? logger = null)
        => new(client, options ?? ClaudeOptions(), new DisciplineEvidencePromptBuilder(), logger);

    private static OpenAiEvidenceProvider OpenAi(ScriptedLlmClient client, OpenAiProviderOptions? options = null,
        ILogger<OpenAiEvidenceProvider>? logger = null)
        => new(client, options ?? OpenAiOptions(), new DisciplineEvidencePromptBuilder(), logger);

    // ── Configuration ─────────────────────────────────────────────────────────

    [Fact]
    public void Real_providers_are_disabled_by_default()
    {
        Assert.False(new ClaudeProviderOptions().Enabled);
        Assert.False(new OpenAiProviderOptions().Enabled);
        Assert.Equal("ANTHROPIC_API_KEY", new ClaudeProviderOptions().ApiKeyEnvironmentVariable);
        Assert.Equal("OPENAI_API_KEY", new OpenAiProviderOptions().ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void A_selected_provider_without_a_key_reports_the_variable_name_not_the_secret()
    {
        var options = new OpenAiProviderOptions { Enabled = true, ApiKeyEnvironmentVariable = "EC_TEST_MISSING_KEY" };
        var provider = OpenAi(new ScriptedLlmClient(), options);

        Assert.False(provider.IsAvailable);
        Assert.Contains("EC_TEST_MISSING_KEY", provider.UnavailableReason);
        Assert.DoesNotContain(TestKeyValue, provider.UnavailableReason);
    }

    [Fact]
    public void An_unselected_provider_needs_no_key_and_simply_stays_unavailable()
    {
        var provider = Claude(new ScriptedLlmClient(), new ClaudeProviderOptions()); // disabled, no key
        Assert.False(provider.IsAvailable);
        Assert.Contains("disabled", provider.UnavailableReason);
    }

    [Fact]
    public async Task Model_and_output_token_configuration_are_respected()
    {
        var client = new ScriptedLlmClient().Returns(ValidSecurityJson);
        var options = ClaudeOptions();
        options.MaxOutputTokens = 1234;

        var provider = Claude(client, options);
        await provider.CollectAsync(Request());

        Assert.Equal("claude-test-model", provider.Metadata.Version);
        Assert.Equal("claude-test-model", client.Requests[0].Model);
        Assert.Equal(1234, client.Requests[0].MaxOutputTokens);
    }

    // ── Planning (metadata-driven; no provider-name branching) ────────────────

    [Fact]
    public void Both_real_providers_are_discipline_scoped_with_all_disciplines()
    {
        foreach (var metadata in new[] { Claude(new ScriptedLlmClient()).Metadata, OpenAi(new ScriptedLlmClient()).Metadata })
        {
            Assert.Equal(EvidenceProviderType.LLM, metadata.ProviderType);
            Assert.Equal(EvidenceAcquisitionScope.Discipline, metadata.DefaultAcquisitionScope);
            Assert.True(metadata.RequiresAnalyzerInstructions);
            Assert.False(metadata.SupportsRepositoryWideAnalysis);
            Assert.True(metadata.Supports(FindingCategory.Security) && metadata.Supports(FindingCategory.Architecture));
        }
    }

    [Fact]
    public void Claude_and_openai_plan_independent_steps_per_discipline()
    {
        IReadOnlyCollection<IEvidenceProvider> providers = [Claude(new ScriptedLlmClient()), OpenAi(new ScriptedLlmClient())];
        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, providers,
            [FindingCategory.Architecture, FindingCategory.Security]);

        Assert.Equal(4, plan.Steps.Count);                       // 2 providers × 2 disciplines
        Assert.Equal(4, plan.DisciplineScopedSteps);
        Assert.Equal(2, plan.Steps.Count(s => s.ProviderName == "Claude"));
        Assert.Equal(2, plan.Steps.Count(s => s.ProviderName == "OpenAI"));
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    [Fact]
    public async Task Prompt_carries_discipline_schema_and_only_the_selected_context()
    {
        var client = new ScriptedLlmClient().Returns(ValidSecurityJson);
        await Claude(client).CollectAsync(Request());

        var request = Assert.Single(client.Requests);
        Assert.Contains("Security", request.UserContent);                    // discipline
        Assert.Contains("schemaVersion", request.UserContent);               // structured schema
        Assert.Contains("src/A.cs", request.UserContent);                    // selected context
        Assert.DoesNotContain("src/NotSelected.cs", request.UserContent);    // omitted file never sent
        Assert.DoesNotContain("NeverSent", request.UserContent);

        // Guardrails: no Markdown, no invented references, empty output allowed.
        Assert.Contains("no Markdown", request.UserContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never invent", request.UserContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observations\": []", request.UserContent);

        // System instruction: an evidence source, not the reviewer.
        Assert.Contains("evidence acquisition source", request.SystemInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT the reviewer", request.SystemInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_observation_set_is_valid()
    {
        var client = new ScriptedLlmClient().Returns("""{"schemaVersion":"1.0","discipline":"Security","observations":[]}""");
        var evidence = Assert.Single(await Claude(client).CollectAsync(Request()));

        Assert.True(evidence.Success);
        Assert.Equal("0", evidence.Metadata["observationCount"]);
    }

    // ── Structured response validation ────────────────────────────────────────

    [Fact]
    public async Task Valid_direct_json_is_accepted()
    {
        var evidence = Assert.Single(await Claude(new ScriptedLlmClient().Returns(ValidSecurityJson)).CollectAsync(Request()));
        Assert.Contains("Hardcoded secret", evidence.RawResponse);
        Assert.Equal("1.0", evidence.Metadata["responseSchemaVersion"]);
    }

    [Fact]
    public async Task A_single_fenced_json_block_is_accepted()
    {
        var fenced = "```json\n" + ValidSecurityJson + "\n```";
        var evidence = Assert.Single(await Claude(new ScriptedLlmClient().Returns(fenced)).CollectAsync(Request()));
        Assert.StartsWith("{", evidence.RawResponse.TrimStart());
    }

    [Theory]
    [InlineData("not json at all", "no JSON object")]
    [InlineData("""{"schemaVersion":"9.9","discipline":"Security","observations":[]}""", "Unsupported response schemaVersion")]
    [InlineData("""{"discipline":"Security","observations":[]}""", "missing 'schemaVersion'")]
    [InlineData("""{"schemaVersion":"1.0","observations":[]}""", "missing 'discipline'")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Testing","observations":[]}""", "Discipline mismatch")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security"}""", "missing an 'observations' array")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security","observations":[{"title":"t","severity":"Catastrophic"}]}""", "invalid 'severity'")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security","observations":[{"title":"t","confidence":"92"}]}""", "invalid 'confidence'")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security","observations":[{"description":"no title"}]}""", "missing a 'title'")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security","observations":[{"title":"t","fileReferences":[{"line":3}]}]}""", "without a 'path'")]
    [InlineData("""{"schemaVersion":"1.0","discipline":"Security","observations":[ """, "incomplete or unbalanced")]
    public async Task Invalid_structured_responses_are_rejected_with_a_categorized_error(string response, string expectedFragment)
    {
        var provider = Claude(new ScriptedLlmClient().Returns(response));

        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CollectAsync(Request()));
        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
        Assert.Contains(expectedFragment, error.Message);
    }

    [Fact]
    public async Task Multiple_json_documents_are_rejected_as_ambiguous()
    {
        var two = ValidSecurityJson + "\n" + ValidSecurityJson;
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(new ScriptedLlmClient().Returns(two)).CollectAsync(Request()));

        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
        Assert.Contains("multiple JSON documents", error.Message);
    }

    [Fact]
    public async Task A_truncated_response_is_rejected()
    {
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(new ScriptedLlmClient().Returns(ValidSecurityJson, finishReason: "max_tokens", truncated: true))
                .CollectAsync(Request()));

        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
        Assert.Contains("truncated", error.Message);
    }

    // ── Structured repair (at most once) ──────────────────────────────────────

    [Fact]
    public async Task One_repair_attempt_can_rescue_an_invalid_response()
    {
        var client = new ScriptedLlmClient().Returns("oops, not json").Returns(ValidSecurityJson);
        var evidence = Assert.Single(await Claude(client, ClaudeOptions(repair: true)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);
        Assert.Equal("true", evidence.Metadata["repairAttempted"]);
        Assert.Contains("failed structured-output validation", client.Requests[1].UserContent);
        Assert.Contains("Do NOT analyze", client.Requests[1].UserContent);
    }

    [Fact]
    public async Task Repair_is_attempted_at_most_once_and_then_categorized()
    {
        var client = new ScriptedLlmClient().Returns("still not json").Returns("also not json");
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(client, ClaudeOptions(repair: true)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);                       // never a repair loop
        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
        Assert.Contains("after one repair attempt", error.Message);
    }

    // ── Execution: retry, timeout, cancellation, auth ─────────────────────────

    [Fact]
    public async Task A_rate_limit_is_retried_and_then_succeeds()
    {
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.RateLimit).Returns(ValidSecurityJson);
        var evidence = Assert.Single(await Claude(client, ClaudeOptions(maxRetries: 2)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);
        Assert.Equal("1", evidence.Metadata["retryCount"]);
    }

    [Fact]
    public async Task An_authorization_failure_is_never_retried()
    {
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.Authentication, "invalid api key");
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(client, ClaudeOptions(maxRetries: 3)).CollectAsync(Request()));

        Assert.Equal(1, client.CallCount);                       // not transient → no retry
        Assert.Equal(LlmErrorCategory.Authentication, error.Category);
    }

    [Fact]
    public async Task A_timeout_is_retried_then_categorized()
    {
        var client = new ScriptedLlmClient().TimesOut().TimesOut();
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(client, ClaudeOptions(maxRetries: 1)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);                       // initial + one retry
        Assert.Equal(LlmErrorCategory.Timeout, error.Category);
    }

    [Fact]
    public async Task Run_cancellation_propagates_and_stops_the_provider()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new ScriptedLlmClient().Returns(ValidSecurityJson);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Claude(client).CollectAsync(Request(), cts.Token));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task A_provider_timeout_reported_as_cancelled_is_reclassified_and_retried()
    {
        // Real transports surface a timeout as Cancelled (they only see the linked
        // timeout token); with the RUN token alive that must be a retried TIMEOUT.
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.Cancelled).Returns(ValidSecurityJson);
        var evidence = Assert.Single(await Claude(client, ClaudeOptions(maxRetries: 2)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);                    // initial + one retry
        Assert.Equal("1", evidence.Metadata["retryCount"]);
    }

    [Fact]
    public async Task A_provider_timeout_reported_as_cancelled_exhausts_retries_as_timeout()
    {
        var client = new ScriptedLlmClient().Fails(LlmErrorCategory.Cancelled).Fails(LlmErrorCategory.Cancelled);
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(client, ClaudeOptions(maxRetries: 1)).CollectAsync(Request()));

        Assert.Equal(2, client.CallCount);                    // initial + one retry
        Assert.Equal(LlmErrorCategory.Timeout, error.Category);
        Assert.DoesNotContain("cancelled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_cancellation_reported_as_cancelled_is_never_retried()
    {
        using var cts = new CancellationTokenSource();
        var client = new ScriptedLlmClient().CancelsThenFails(cts);
        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(client, ClaudeOptions(maxRetries: 3)).CollectAsync(Request(), cts.Token));

        Assert.Equal(1, client.CallCount);                    // never retried
        Assert.Equal(LlmErrorCategory.Cancelled, error.Category);
    }

    [Fact]
    public async Task Telemetry_distinguishes_provider_timeout_from_run_cancellation()
    {
        var timeoutLogger = new CapturingLogger<ClaudeEvidenceProvider>();
        var timeoutClient = new ScriptedLlmClient().Fails(LlmErrorCategory.Cancelled).Fails(LlmErrorCategory.Cancelled);
        await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(timeoutClient, ClaudeOptions(maxRetries: 1), timeoutLogger).CollectAsync(Request()));

        Assert.Contains(timeoutLogger.Entries, e => e.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));

        var cancelLogger = new CapturingLogger<ClaudeEvidenceProvider>();
        using var cts = new CancellationTokenSource();
        var cancelClient = new ScriptedLlmClient().CancelsThenFails(cts);
        await Assert.ThrowsAsync<LlmProviderException>(
            () => Claude(cancelClient, ClaudeOptions(maxRetries: 3), cancelLogger).CollectAsync(Request(), cts.Token));

        Assert.Contains(cancelLogger.Entries, e => e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cancelLogger.Entries, e => e.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    // ── Telemetry (provider-neutral; never secrets) ───────────────────────────

    [Fact]
    public async Task Telemetry_records_provider_model_usage_and_ids_when_available()
    {
        var client = new ScriptedLlmClient("openai").Returns(
            ValidSecurityJson, inputTokens: 1200, outputTokens: 300, finishReason: "stop",
            responseId: "resp_123", requestId: "req_456");

        var evidence = Assert.Single(await OpenAi(client).CollectAsync(Request()));

        Assert.Equal("OpenAI", evidence.ProviderName);
        Assert.Equal("openai", evidence.ProviderId);
        Assert.Equal("openai-test-model", evidence.ProviderVersion);
        Assert.Equal("1200", evidence.Metadata["inputTokens"]);
        Assert.Equal("300", evidence.Metadata["outputTokens"]);
        Assert.Equal(1500, evidence.TokensUsed);
        Assert.Equal("resp_123", evidence.Metadata["responseId"]);
        Assert.Equal("req_456", evidence.Metadata["requestId"]);
        Assert.Equal("stop", evidence.Metadata["finishReason"]);
        Assert.Equal("1", evidence.Metadata["contextFileCount"]);
    }

    [Fact]
    public async Task Unavailable_usage_is_omitted_rather_than_invented()
    {
        var evidence = Assert.Single(await Claude(new ScriptedLlmClient().Returns(ValidSecurityJson)).CollectAsync(Request()));

        Assert.False(evidence.Metadata.ContainsKey("inputTokens"));
        Assert.False(evidence.Metadata.ContainsKey("outputTokens"));
        Assert.False(evidence.Metadata.ContainsKey("responseId"));
        Assert.Null(evidence.TokensUsed);
        Assert.Null(evidence.CostEstimate);
    }

    [Fact]
    public async Task No_secret_ever_reaches_the_evidence_or_its_metadata()
    {
        var evidence = Assert.Single(await Claude(new ScriptedLlmClient().Returns(ValidSecurityJson)).CollectAsync(Request()));

        Assert.DoesNotContain(TestKeyValue, evidence.RawResponse);
        Assert.All(evidence.Metadata, kv => Assert.DoesNotContain(TestKeyValue, kv.Value));
        Assert.DoesNotContain(TestKeyVariable, evidence.RawResponse);
    }
}
