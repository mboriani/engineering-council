using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Reliability observations. See <c>prompts/reliability-analyzer.md</c>.</summary>
public sealed class ReliabilityAnalyzer(ILogger<ReliabilityAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "reliability-analyzer";
    public override FindingCategory Category => FindingCategory.Reliability;
}
