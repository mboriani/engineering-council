using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Interpretation;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class EvidenceInterpreterTests
{
    private static AnalyzerContext ContextWith(params string[] files) => new()
    {
        Snapshot = new RepositorySnapshot
        {
            RootPath = "/repo",
            SolutionName = "Repo",
            Files = files.Select(p => new ScannedFile
            {
                RelativePath = p, Extension = Path.GetExtension(p), SizeBytes = 1, LineCount = 1
            }).ToList()
        }
    };

    private static Evidence LlmEvidence(string raw) => new()
    {
        ProviderName = "Mock", ProviderType = EvidenceProviderType.LLM, RawResponse = raw, Success = true
    };

    private readonly StructuredLlmEvidenceInterpreter _interpreter = new();

    [Fact]
    public async Task One_evidence_produces_one_observation()
    {
        var raw = """
        { "observations": [
          { "type": "HighComplexity", "discipline": "CodeQuality", "title": "Complex method",
            "description": "d", "severity": "Medium", "confidence": "Medium", "ruleId": "R1",
            "fileReferences": [ { "path": "src/A.cs" } ] } ] }
        """;

        var obs = await _interpreter.InterpretAsync(LlmEvidence(raw), ContextWith("src/A.cs"));

        var single = Assert.Single(obs);
        Assert.Equal("HighComplexity", single.ObservationType);
        Assert.Equal(FindingCategory.CodeQuality, single.Discipline);
        Assert.Equal("R1", single.RuleId);
        Assert.Equal("Mock", single.SourceProvider);
        Assert.Equal("src/A.cs", Assert.Single(single.FileReferences).Path);
    }

    [Fact]
    public async Task One_evidence_produces_multiple_observations()
    {
        var raw = """
        { "observations": [
          { "title": "a", "discipline": "Security", "severity": "High" },
          { "title": "b", "discipline": "Testing", "severity": "Low" },
          { "title": "c", "discipline": "Architecture", "severity": "Medium" } ] }
        """;

        var obs = await _interpreter.InterpretAsync(LlmEvidence(raw), ContextWith());

        Assert.Equal(3, obs.Count);
    }

    [Fact]
    public async Task Malformed_structured_evidence_yields_no_observations()
    {
        var obs = await _interpreter.InterpretAsync(LlmEvidence("not json { observations "), ContextWith());
        Assert.Empty(obs);
    }

    [Fact]
    public async Task Invented_file_references_are_dropped_and_confidence_is_lowered()
    {
        var raw = """
        { "observations": [
          { "title": "x", "description": "complete", "discipline": "CodeQuality", "confidence": "High",
            "fileReferences": [ { "path": "src/Invented.cs" } ] } ] }
        """;

        var obs = await _interpreter.InterpretAsync(LlmEvidence(raw), ContextWith("src/Real.cs"));

        var o = Assert.Single(obs);
        Assert.Empty(o.FileReferences);                       // invented path dropped
        Assert.Equal(FindingConfidence.Medium, o.Confidence); // lowered from High
    }

    [Fact]
    public void Interpreter_only_claims_llm_evidence()
    {
        Assert.True(_interpreter.CanInterpret(LlmEvidence("{\"observations\":[]}")));
        Assert.False(_interpreter.CanInterpret(new Evidence
        {
            ProviderName = "Roslyn", ProviderType = EvidenceProviderType.StaticAnalyzer,
            RawResponse = "<sarif/>", Success = true
        }));
        Assert.False(_interpreter.CanInterpret(new Evidence { ProviderName = "Mock", Success = false }));
    }

    [Fact]
    public void Resolver_discovers_the_right_interpreter_and_reports_unsupported()
    {
        var resolver = new EvidenceInterpreterResolver([_interpreter]);

        Assert.NotNull(resolver.Resolve(LlmEvidence("{\"observations\":[]}")));
        // Static-analyzer evidence has no registered interpreter → unsupported.
        Assert.Null(resolver.Resolve(new Evidence
        {
            ProviderName = "Sonar", ProviderType = EvidenceProviderType.StaticAnalyzer,
            RawResponse = "report", Success = true
        }));
    }
}
