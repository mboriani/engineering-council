namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Overall engineering risk implied by the findings. Computed by a documented,
/// rule-based scorer (no LLM). Ordered lowest → highest.
/// </summary>
public enum EngineeringRisk
{
    Low = 0,
    Moderate = 1,
    High = 2,
    Critical = 3
}
