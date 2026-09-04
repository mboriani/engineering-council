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
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// OPT-IN integration tests that call the REAL provider APIs. They are inert unless
/// explicitly enabled, so the normal suite never needs internet, credentials, or paid
/// model calls:
/// <code>
///   RUN_CLAUDE_INTEGRATION_TESTS=true   ANTHROPIC_API_KEY=...
///   RUN_OPENAI_INTEGRATION_TESTS=true   OPENAI_API_KEY=...
/// </code>
/// They assert STRUCTURAL and SEMANTIC invariants (valid schema, provider-neutral
/// observations, a consumable package) — never exact model wording.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LlmIntegrationTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-int-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-int-out-" + Guid.NewGuid().ToString("N"));

    public LlmIntegrationTests()
    {
        // A deliberately tiny, deterministic fixture repository.
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Fixture.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "PaymentClient.cs"), """
            using System.Net.Http;

            public sealed class PaymentClient
            {
                private static readonly HttpClient Http = new HttpClient();
                private const string ApiKey = "hardcoded-demo-value";

                public async Task<string> ChargeAsync(string account)
                {
                    var response = await Http.GetAsync($"https://payments.example.com/charge?account={account}&key={ApiKey}");
                    return await response.Content.ReadAsStringAsync();
                }
            }
            """);
    }

    private static bool Enabled(string flag, string keyVariable)
        => string.Equals(Environment.GetEnvironmentVariable(flag), "true", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(keyVariable));

    [Fact]
    public async Task Claude_real_call_produces_a_consumable_package()
    {
        if (!Enabled("RUN_CLAUDE_INTEGRATION_TESTS", "ANTHROPIC_API_KEY"))
            return; // skipped: opt-in only (flag and/or credentials absent)

        var options = new ClaudeProviderOptions { Enabled = true, MaxOutputTokens = 1500, TimeoutSeconds = 120, MaxRetries = 1 };
        if (Environment.GetEnvironmentVariable("EC_CLAUDE_MODEL") is { Length: > 0 } model) options.Model = model;

        var provider = new ClaudeEvidenceProvider(new ClaudeClient(options), options, new DisciplineEvidencePromptBuilder());
        await AssertRealProviderRunAsync(provider, FindingCategory.Security);
    }

    [Fact]
    public async Task OpenAi_real_call_produces_a_consumable_package()
    {
        if (!Enabled("RUN_OPENAI_INTEGRATION_TESTS", "OPENAI_API_KEY"))
            return; // skipped: opt-in only (flag and/or credentials absent)

        var options = new OpenAiProviderOptions { Enabled = true, MaxOutputTokens = 1500, TimeoutSeconds = 120, MaxRetries = 1 };
        if (Environment.GetEnvironmentVariable("EC_OPENAI_MODEL") is { Length: > 0 } model) options.Model = model;

        var provider = new OpenAiEvidenceProvider(new OpenAiClient(options), options, new DisciplineEvidencePromptBuilder());
        await AssertRealProviderRunAsync(provider, FindingCategory.Reliability);
    }

    /// <summary>Runs the full pipeline for ONE discipline with a small context and asserts invariants.</summary>
    private async Task AssertRealProviderRunAsync(IEvidenceProvider provider, FindingCategory discipline)
    {
        Assert.True(provider.IsAvailable, provider.UnavailableReason);

        var evidenceOptions = new EvidenceOptions
        {
            Providers = [provider.Metadata.Name],
            Disciplines = [discipline],
            ContextMaxFiles = 5,
            ContextMaxCharacters = 20_000
        };
        var factory = new EvidenceProviderFactory([provider]);

        var pipeline = new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(new ContextContentPolicy { MaxFiles = 5, MaxCharacters = 20_000 }), evidenceOptions),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver([new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new ReliabilityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            evidenceOptions,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = provider.Metadata.Name });

        // Structural invariants only — never exact model wording.
        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);
        Assert.Single(result.Run.AcquisitionPlan!.Steps);                     // one discipline → one step
        var execution = Assert.Single(result.Run.ProviderExecution!.Records);
        Assert.True(execution.Success, execution.ErrorMessage);

        var evidence = Assert.Single(result.Run.Evidence);
        Assert.Equal(provider.Metadata.Name, evidence.ProviderName);
        Assert.Equal("1.0", evidence.Metadata["responseSchemaVersion"]);      // validated schema
        Assert.Equal(provider.Metadata.Version, evidence.ProviderVersion);    // configured model recorded

        // Observations (possibly zero — a valid answer) stay provider-neutral.
        Assert.All(result.Run.Observations, o => Assert.Equal(provider.Metadata.Name, o.SourceProvider));

        // The package is produced and consumable by the external contract.
        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);

        // No credential ever reaches the artifact.
        foreach (var variable in new[] { "ANTHROPIC_API_KEY", "OPENAI_API_KEY" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 8 } key)
                Assert.DoesNotContain(key, packageJson);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
