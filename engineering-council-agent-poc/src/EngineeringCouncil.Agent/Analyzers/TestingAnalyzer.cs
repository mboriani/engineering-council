using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Testing observations. See <c>prompts/testing-analyzer.md</c>.</summary>
public sealed class TestingAnalyzer(ILogger<TestingAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "testing-analyzer";
    public override FindingCategory Category => FindingCategory.Testing;
}
