namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The engineering dimension a finding belongs to. Kept broad on purpose so
/// future specialist agents (security scanner, performance profiler, etc.) can
/// map their output onto a shared taxonomy consumable by the dashboard.
/// </summary>
public enum FindingCategory
{
    Architecture = 0,
    CodeQuality = 1,
    Maintainability = 2,
    Testing = 3,
    Performance = 4,
    Security = 5,
    Reliability = 6,
    Observability = 7,
    Dependencies = 8,
    Documentation = 9,
    DeveloperExperience = 10,

    /// <summary>
    /// The discipline could not be determined deterministically (e.g. an external
    /// tool rule with no recognized signal). Observations stay Unknown rather than
    /// being guessed; no analyzer claims them.
    /// </summary>
    Unknown = 11
}
