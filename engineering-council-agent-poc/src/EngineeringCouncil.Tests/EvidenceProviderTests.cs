using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.Evidence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class EvidenceProviderTests
{
    private static EvidenceRequest RequestFor(FindingCategory category)
    {
        var file = new ScannedFile { RelativePath = "src/Service.cs", Extension = ".cs", SizeBytes = 1, LineCount = 2, Content = "class Service {}" };
        return new EvidenceRequest
        {
            RunId = "run1",
            RepositorySnapshot = new RepositorySnapshot { RootPath = "/r", SolutionName = "R", Files = [file] },
            Scope = EvidenceAcquisitionScope.Discipline,
            Discipline = category,
            Instructions = $"Objective ({category})",
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "test",
                Files = [file],
                TotalRepositoryFiles = 1,
                SelectedFileCount = 1,
                EstimatedContentSize = file.Content!.Length
            },
            ProviderNames = ["Mock"],
            CorrelationId = $"Mock:{category}"
        };
    }

    // ── Provider resolution ─────────────────────────────────────────────────────

    [Fact]
    public void Factory_resolves_provider_by_name_case_insensitively()
    {
        var factory = new EvidenceProviderFactory([new MockEvidenceProvider()]);

        Assert.Equal("Mock", factory.GetProvider("Mock").Metadata.Name);
        Assert.Equal("Mock", factory.GetProvider("mock").Metadata.Name);
        Assert.True(factory.TryGetProvider("MOCK", out _));
    }

    [Fact]
    public void Factory_throws_for_invalid_provider()
    {
        var factory = new EvidenceProviderFactory([new MockEvidenceProvider()]);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetProvider("DoesNotExist"));
        Assert.Contains("DoesNotExist", ex.Message);
        Assert.False(factory.TryGetProvider("DoesNotExist", out _));
    }

    // ── Mock provider ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Mock_provider_returns_discipline_scoped_observations_not_findings()
    {
        var provider = new MockEvidenceProvider();

        var evidence = Assert.Single(await provider.CollectAsync(RequestFor(FindingCategory.Security)));

        Assert.Equal("Mock", evidence.ProviderName);
        Assert.Equal(EvidenceProviderType.LLM, evidence.ProviderType);
        Assert.Equal(EvidenceAcquisitionScope.Discipline, provider.Metadata.DefaultAcquisitionScope);
        Assert.Contains("observations", evidence.RawResponse); // raw text: observations, not findings
        Assert.Contains("Security", evidence.RawResponse);      // this discipline only
        Assert.DoesNotContain("Architecture", evidence.RawResponse);
        Assert.DoesNotContain("\"findings\"", evidence.RawResponse);
    }

    // ── Future providers ────────────────────────────────────────────────────────

    [Fact]
    public async Task Unimplemented_future_provider_throws_NotImplemented()
    {
        var provider = new OllamaEvidenceProvider();

        Assert.False(provider.IsAvailable);
        await Assert.ThrowsAsync<NotImplementedException>(
            () => provider.CollectAsync(RequestFor(FindingCategory.CodeQuality)));
    }

    // ── Configuration override + effective-default resolution ───────────────────

    [Fact]
    public void Configuration_override_selects_mock_provider()
    {
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddEngineeringCouncil(o => o.Providers = ["Mock"])
            .BuildServiceProvider();

        var evidenceOptions = sp.GetRequiredService<EvidenceOptions>();
        var selected = sp.GetRequiredService<SelectedProviders>();

        Assert.Equal(["Mock"], evidenceOptions.Providers);
        Assert.True(selected.IsMock);
    }

    [Fact]
    public void Unconfigured_run_defaults_to_the_offline_mock_provider()
    {
        // External providers are never the zero-config default (Milestone 011).
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddEngineeringCouncil()
            .BuildServiceProvider();

        Assert.Equal(["Mock"], sp.GetRequiredService<EvidenceOptions>().Providers);
    }

    [Fact]
    public void Explicitly_selected_claude_without_a_key_stays_selected_and_reports_why()
    {
        // Milestone 011: an explicitly selected real provider is NOT silently swapped
        // for Mock — it stays selected so the run reports a clear, secret-safe reason.
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddEngineeringCouncil(o => o.Providers = ["Claude"])
            .BuildServiceProvider();

        Assert.Equal(["Claude"], sp.GetRequiredService<EvidenceOptions>().Providers);

        var claude = sp.GetRequiredService<IEvidenceProviderFactory>().GetProvider("Claude");
        Assert.False(claude.IsAvailable);
        Assert.Contains("ANTHROPIC_API_KEY", claude.UnavailableReason);
    }

    [Fact]
    public void All_providers_including_future_ones_are_registered()
    {
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddEngineeringCouncil()
            .BuildServiceProvider();

        var factory = sp.GetRequiredService<IEvidenceProviderFactory>();

        foreach (var name in new[] { "Claude", "OpenAI", "Mock", "Ollama", "Roslyn", "Sonar", "Semgrep", "NDepend", "SARIF", "Git", "Coverage" })
            Assert.True(factory.TryGetProvider(name, out _), $"missing {name}");
    }

    // ── Analyzer independence ───────────────────────────────────────────────────

    [Fact]
    public async Task Analyzer_consumes_observations_and_never_touches_evidence_or_providers()
    {
        // The analyzer's only input is observations — it neither acquires evidence
        // nor knows any provider. It selects its discipline and preserves provenance.
        var context = new AnalyzerContext { Snapshot = new RepositorySnapshot { RootPath = "/r", SolutionName = "R" } };
        IReadOnlyList<EngineeringObservation> observations =
        [
            new() { Id = "OBS-001", Discipline = FindingCategory.Architecture, ObservationType = "LayerViolation",
                Title = "Bad layering", Severity = FindingSeverity.High, SourceProvider = "Mock", RuleId = "ARCH-1" },
            new() { Id = "OBS-002", Discipline = FindingCategory.Security, ObservationType = "HardcodedSecret",
                Title = "Secret", SourceProvider = "Mock" }, // different discipline — ignored
        ];

        var findings = await new ArchitectureAnalyzer().AnalyzeAsync(observations, context);

        var finding = Assert.Single(findings);
        Assert.Equal("architecture-analyzer", finding.SourceAgent);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Contains("OBS-001", finding.ObservationIds);        // provenance preserved
        Assert.Contains("ARCH-1", finding.SourceRules);
        Assert.Contains("Mock", finding.SupportingProviders);
        Assert.Equal(1, finding.SupportingObservationCount);
    }
}
