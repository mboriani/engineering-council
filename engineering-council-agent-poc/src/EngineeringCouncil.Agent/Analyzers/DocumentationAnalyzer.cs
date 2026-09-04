using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Documentation observations. See <c>prompts/documentation-analyzer.md</c>.</summary>
public sealed class DocumentationAnalyzer(ILogger<DocumentationAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "documentation-analyzer";
    public override FindingCategory Category => FindingCategory.Documentation;
}
