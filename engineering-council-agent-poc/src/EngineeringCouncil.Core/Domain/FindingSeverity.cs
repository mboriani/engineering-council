namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// How impactful an engineering finding is if left unaddressed.
/// Ordered from least to most severe so it can be sorted numerically.
/// </summary>
public enum FindingSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
