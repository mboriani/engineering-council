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
using EngineeringCouncil.Tests.Fakes;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 011 acceptance — real-provider evidence flowing through the WHOLE
/// platform: Claude + OpenAI (discipline-scoped) + SARIF (repository-scoped) in one
/// run, interpreted into provider-neutral observations, analyzed by the existing
/// analyzers, deterministically reconciled, and ending as one
/// engineering-review-package.json the external consumer DTO can read.
/// All model calls go through scripted clients — no network, no credentials.
/// </summary>
public sealed class LlmPipelineTests : IDisposable
{
    private const string KeyVariable = "EC_TEST_PIPELINE_KEY";
    private const string KeyValue = "pipeline-test-key-not-a-real-secret";

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-llm-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-llm-out-" + Guid.NewGuid().ToString("N"));
    private readonly string _sarif = Path.Combine(Path.GetTempPath(), $"ec-llm-{Guid.NewGuid():N}.sarif");

    public LlmPipelineTests()
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Payments.cs"), "public class PaymentClient { }\n");
        File.WriteAllText(_sarif, """
        {
          "version": "2.1.0",
          "runs": [ {
            "tool": { "driver": { "name": "SecScan", "version": "1.0", "rules": [
              { "id": "REL-104", "name": "MissingTimeout", "shortDescription": { "text": "Missing timeout." },
                "properties": { "tags": ["reliability"] } } ] } },
            "results": [ { "ruleId": "REL-104", "ruleIndex": 0, "level": "warning",
              "message": { "text": "Outbound call without a timeout." },
              "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "src/Payments.cs" }, "region": { "startLine": 1 } } } ] } ]
          } ]
        }
        """);
    }

    private static string Observations(string discipline, string type, string title)
        => $$"""
        { "schemaVersion": "1.0", "discipline": "{{discipline}}",
          "observations": [ { "type": "{{type}}", "discipline": "{{discipline}}", "title": "{{title}}",
            "description": "Reported by an LLM evidence source.", "severity": "High", "confidence": "Medium",
            "ruleId": "REL-104",
            "fileReferences": [ { "path": "src/Payments.cs", "startLine": 1 } ] } ] }
        """;

    private static LlmProviderOptions Usable(LlmProviderOptions options)
    {
        options.Enabled = true;
        options.ApiKeyEnvironmentVariable = KeyVariable;
        options.MaxRetries = 0;
        options.TimeoutSeconds = 5;
        options.EnableStructuredRepair = false;
        return options;
    }

    private AnalysisPipeline BuildPipeline(
        IReadOnlyList<IEvidenceProvider> providers, ProviderFailureMode failureMode = ProviderFailureMode.Continue)
    {
        var evidenceOptions = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security, FindingCategory.Reliability],
            ProviderFailureMode = failureMode
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), evidenceOptions),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter(), new SarifEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new ReliabilityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            evidenceOptions,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    // ── Scenario C: Claude + OpenAI + SARIF in one run ────────────────────────

    [Fact]
    public async Task Claude_openai_and_sarif_produce_one_consolidated_package()
    {
        var claudeClient = new ScriptedLlmClient("claude")
            .Returns(Observations("Security", "HardcodedSecret", "Payment credential in source"))
            .Returns(Observations("Reliability", "MissingTimeout", "Outbound call has no explicit timeout"));
        var openAiClient = new ScriptedLlmClient("openai")
            .Returns(Observations("Security", "HardcodedSecret", "Payment credential in source"))
            .Returns(Observations("Reliability", "MissingTimeout", "PaymentClient may wait indefinitely"));

        var providers = new IEvidenceProvider[]
        {
            new ClaudeEvidenceProvider(claudeClient, (ClaudeProviderOptions)Usable(new ClaudeProviderOptions()), new DisciplineEvidencePromptBuilder()),
            new OpenAiEvidenceProvider(openAiClient, (OpenAiProviderOptions)Usable(new OpenAiProviderOptions()), new DisciplineEvidencePromptBuilder()),
            new SarifEvidenceProvider(new SarifOptions { Enabled = true, Files = [_sarif] }),
        };

        var result = await BuildPipeline(providers).RunAsync(
            new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude, OpenAI, SARIF" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);

        // 2 LLM providers × 2 disciplines + SARIF once = 5 provider executions.
        Assert.Equal(5, result.Run.ProviderExecution!.TotalExecutions);
        Assert.Equal(2, claudeClient.CallCount);
        Assert.Equal(2, openAiClient.CallCount);
        Assert.Equal(1, result.Run.ProviderExecution.Records.Count(r => r.ProviderName == "SARIF"));

        // Observations from all three sources, provider-neutral after interpretation.
        Assert.Contains(result.Run.Observations, o => o.SourceProvider == "Claude");
        Assert.Contains(result.Run.Observations, o => o.SourceProvider == "OpenAI");
        Assert.Contains(result.Run.Observations, o => o.SourceProvider == "SARIF");
        Assert.All(result.Run.Observations, o => Assert.IsType<EngineeringObservation>(o));

        // Existing analyzers consumed them; reconciliation ran; package uses consolidated findings.
        Assert.NotEmpty(result.Run.RawFindings);
        Assert.NotNull(result.Run.ReconciliationSummary);
        Assert.Equal(result.Run.Findings.Count, result.Package.Findings.Count);
        Assert.Contains(result.Package.Findings, f => f.AgreementCount >= 2);   // corroborated across sources

        // One package, readable by the external consumer contract.
        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.NotEmpty(dto.Findings);

        // No secrets and no provider SDK types leak into the integration artifact.
        Assert.DoesNotContain(KeyValue, packageJson);
        Assert.DoesNotContain(KeyVariable, packageJson);
        foreach (var leak in new[] { "$type", "System.", "anthropic-version", "x-api-key", "Authorization" })
            Assert.DoesNotContain(leak, packageJson);
    }

    // ── Failure isolation + failure modes ─────────────────────────────────────

    [Fact]
    public async Task Continue_mode_keeps_the_run_going_when_a_provider_cannot_execute()
    {
        var openAiClient = new ScriptedLlmClient("openai")
            .Returns(Observations("Security", "HardcodedSecret", "Payment credential in source"))
            .Returns(Observations("Reliability", "MissingTimeout", "No timeout configured"));

        // Claude is selected but has no key → unusable.
        var claudeOptions = new ClaudeProviderOptions { Enabled = true, ApiKeyEnvironmentVariable = "EC_TEST_ABSENT_KEY" };
        var providers = new IEvidenceProvider[]
        {
            new ClaudeEvidenceProvider(new ScriptedLlmClient(), claudeOptions, new DisciplineEvidencePromptBuilder()),
            new OpenAiEvidenceProvider(openAiClient, (OpenAiProviderOptions)Usable(new OpenAiProviderOptions()), new DisciplineEvidencePromptBuilder()),
        };

        var result = await BuildPipeline(providers).RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude, OpenAI" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);

        var claudeFailures = result.Run.ProviderExecution!.Records.Where(r => r.ProviderName == "Claude").ToList();
        Assert.All(claudeFailures, r => Assert.False(r.Success));
        Assert.All(claudeFailures, r => Assert.Contains("EC_TEST_ABSENT_KEY", r.ErrorMessage));

        // OpenAI evidence survived and produced findings.
        Assert.Contains(result.Run.Observations, o => o.SourceProvider == "OpenAI");
        Assert.NotEmpty(result.Package.Findings);
    }

    [Fact]
    public async Task FailRun_mode_fails_the_whole_run_when_a_selected_provider_cannot_execute()
    {
        var claudeOptions = new ClaudeProviderOptions { Enabled = true, ApiKeyEnvironmentVariable = "EC_TEST_ABSENT_KEY" };
        var providers = new IEvidenceProvider[]
        {
            new ClaudeEvidenceProvider(new ScriptedLlmClient(), claudeOptions, new DisciplineEvidencePromptBuilder()),
        };

        var result = await BuildPipeline(providers, ProviderFailureMode.FailRun)
            .RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude" });

        Assert.Equal(AnalysisRunStatus.Failed, result.Run.Status);
        Assert.Contains("FailRun", result.Run.Error);
        Assert.Contains("EC_TEST_ABSENT_KEY", result.Run.Error);
        Assert.DoesNotContain(KeyValue, result.Run.Error);
    }

    public void Dispose()
    {
        try { File.Delete(_sarif); } catch { /* best effort */ }
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
