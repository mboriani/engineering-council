namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Overall engineering health of a repository at a point in time. Computed by a
/// documented, rule-based scorer (no LLM). Ordered best → worst.
/// </summary>
public enum EngineeringHealth
{
    Excellent = 0,
    Good = 1,
    Fair = 2,
    NeedsAttention = 3,
    Critical = 4
}
