using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over CodeQuality observations. See <c>prompts/code-quality-analyzer.md</c>.</summary>
public sealed class CodeQualityAnalyzer(ILogger<CodeQualityAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "code-quality-analyzer";
    public override FindingCategory Category => FindingCategory.CodeQuality;
}
