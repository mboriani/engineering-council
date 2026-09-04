using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Interpretation;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class ObservationPipelineTests
{
    private static AnalyzerContext Context => new()
    {
        Snapshot = new RepositorySnapshot { RootPath = "/repo", SolutionName = "Repo" }
    };

    private static Evidence Llm(string provider, string raw, bool success = true) => new()
    {
        ProviderName = provider, ProviderType = EvidenceProviderType.LLM, RawResponse = raw, Success = success
    };

    private const string TwoObs =
        "{\"observations\":[{\"title\":\"a\",\"discipline\":\"Security\"},{\"title\":\"b\",\"discipline\":\"Testing\"}]}";

    private static EvidenceInterpretationPipeline PipelineWith(params IEvidenceInterpreter[] interpreters)
        => new(new EvidenceInterpreterResolver(interpreters));

    [Fact]
    public async Task Aggregates_observations_and_assigns_unique_ids()
    {
        var pipeline = PipelineWith(new StructuredLlmEvidenceInterpreter());

        var result = await pipeline.InterpretAsync([Llm("Mock", TwoObs)], Context);

        Assert.Equal(2, result.Observations.Count);
        Assert.Equal(["OBS-001", "OBS-002"], result.Observations.Select(o => o.Id));
        Assert.Equal(2, result.Summary.ObservationsProduced);
        Assert.Equal(1, result.Summary.EvidenceProcessed);
    }

    [Fact]
    public async Task Failed_evidence_is_counted_not_interpreted()
    {
        var pipeline = PipelineWith(new StructuredLlmEvidenceInterpreter());

        var result = await pipeline.InterpretAsync([Llm("Mock", "", success: false)], Context);

        Assert.Empty(result.Observations);
        Assert.Equal(1, result.Summary.EvidenceFailed);
        Assert.Equal(0, result.Summary.EvidenceProcessed);
    }

    [Fact]
    public async Task Unsupported_evidence_is_reported_and_produces_no_observations()
    {
        var pipeline = PipelineWith(new StructuredLlmEvidenceInterpreter());

        // Static-analyzer evidence: no interpreter matches.
        var result = await pipeline.InterpretAsync(
        [
            new Evidence { ProviderName = "Sonar", ProviderType = EvidenceProviderType.StaticAnalyzer, RawResponse = "x", Success = true }
        ], Context);

        Assert.Empty(result.Observations);
        Assert.Equal(1, result.Summary.EvidenceUnsupported);
    }

    [Fact]
    public async Task Interpreter_failure_is_isolated()
    {
        var pipeline = PipelineWith(new ThrowingInterpreter(), new StructuredLlmEvidenceInterpreter());

        // First item hits the throwing interpreter; second is interpreted normally.
        var result = await pipeline.InterpretAsync(
        [
            Llm("Boom", "{\"observations\":[{\"title\":\"z\"}]}"),
            Llm("Mock", TwoObs)
        ], Context);

        Assert.Equal(1, result.Summary.InterpreterFailures);
        Assert.Equal(2, result.Observations.Count); // the good evidence still produced observations
    }

    /// <summary>Claims all LLM evidence then throws — to exercise failure isolation.</summary>
    private sealed class ThrowingInterpreter : IEvidenceInterpreter
    {
        public string ProviderName => "throwing";
        public bool CanInterpret(Evidence evidence) => evidence.ProviderName == "Boom";
        public Task<IReadOnlyList<EngineeringObservation>> InterpretAsync(
            Evidence evidence, AnalyzerContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("interpreter boom");
    }
}
