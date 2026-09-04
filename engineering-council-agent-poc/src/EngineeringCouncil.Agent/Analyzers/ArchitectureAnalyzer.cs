using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Architecture observations. See <c>prompts/architecture-analyzer.md</c>.</summary>
public sealed class ArchitectureAnalyzer(ILogger<ArchitectureAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "architecture-analyzer";
    public override FindingCategory Category => FindingCategory.Architecture;
}
