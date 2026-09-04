namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// How confident the analyzer is that the finding is real and actionable.
/// The agent is instructed to lower confidence when evidence is weak.
/// </summary>
public enum FindingConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}
