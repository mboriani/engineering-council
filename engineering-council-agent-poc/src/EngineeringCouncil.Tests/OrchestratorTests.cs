using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class OrchestratorTests
{
    private static AnalyzerContext Context => new()
    {
        Snapshot = new RepositorySnapshot { RootPath = "/repo", SolutionName = "Repo" }
    };

    private sealed class StubAnalyzer(string name, FindingCategory category, int count) : IAnalyzerAgent
    {
        public string Name => name;
        public FindingCategory Category => category;

        public Task<IReadOnlyList<Finding>> AnalyzeAsync(
            IReadOnlyList<EngineeringObservation> observations, AnalyzerContext context, CancellationToken ct = default)
        {
            IReadOnlyList<Finding> findings = Enumerable.Range(1, count)
                .Select(i => new Finding { Id = $"{name}-{i}", Title = $"{name} #{i}", Category = category, SourceAgent = name })
                .ToList();
            return Task.FromResult(findings);
        }
    }

    private sealed class ThrowingAnalyzer : IAnalyzerAgent
    {
        public string Name => "throwing-analyzer";
        public FindingCategory Category => FindingCategory.Reliability;
        public Task<IReadOnlyList<Finding>> AnalyzeAsync(
            IReadOnlyList<EngineeringObservation> observations, AnalyzerContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Aggregates_findings_from_all_analyzers()
    {
        var orchestrator = new AnalysisOrchestrator(
        [
            new StubAnalyzer("a", FindingCategory.Architecture, 2),
            new StubAnalyzer("b", FindingCategory.Security, 3)
        ]);

        var findings = await orchestrator.AnalyzeAsync([], Context);

        Assert.Equal(5, findings.Count);
        Assert.Equal(2, findings.Count(f => f.SourceAgent == "a"));
        Assert.Equal(3, findings.Count(f => f.SourceAgent == "b"));
    }

    [Fact]
    public async Task A_failing_analyzer_is_isolated_and_does_not_sink_the_run()
    {
        var orchestrator = new AnalysisOrchestrator(
        [
            new StubAnalyzer("ok", FindingCategory.Testing, 1),
            new ThrowingAnalyzer()
        ]);

        var findings = await orchestrator.AnalyzeAsync([], Context);

        var finding = Assert.Single(findings);
        Assert.Equal("ok", finding.SourceAgent);
    }
}
