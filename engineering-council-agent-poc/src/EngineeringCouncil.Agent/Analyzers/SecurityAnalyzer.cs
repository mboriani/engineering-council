using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Agent.Analyzers;

/// <summary>Reasons over Security observations (defensive only). See <c>prompts/security-analyzer.md</c>.</summary>
public sealed class SecurityAnalyzer(ILogger<SecurityAnalyzer>? logger = null)
    : ObservationBasedAnalyzer(logger)
{
    public override string Name => "security-analyzer";
    public override FindingCategory Category => FindingCategory.Security;
}
