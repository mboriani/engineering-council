using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Observability observations. See <c>prompts/observability-analyzer.md</c>.</summary>
public sealed class ObservabilityAnalyzer(ILogger<ObservabilityAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "observability-analyzer";
    public override FindingCategory Category => FindingCategory.Observability;
}
