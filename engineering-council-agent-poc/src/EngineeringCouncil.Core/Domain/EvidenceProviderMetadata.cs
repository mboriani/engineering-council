namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Declares a provider's identity and how it wants to be acquired. The
/// acquisition planner reads this (never branches on concrete provider names) to
/// decide the execution topology. Configuration may override
/// <see cref="DefaultAcquisitionScope"/>.
/// </summary>
public sealed record EvidenceProviderMetadata
{
    public required string Name { get; init; }

    public required EvidenceProviderType ProviderType { get; init; }

    /// <summary>Default scope: Repository = once; Discipline = once per discipline.</summary>
    public required EvidenceAcquisitionScope DefaultAcquisitionScope { get; init; }

    /// <summary>Disciplines this source can serve. Empty = all disciplines.</summary>
    public IReadOnlySet<FindingCategory> SupportedDisciplines { get; init; }
        = new HashSet<FindingCategory>();

    /// <summary>True when the source needs discipline-specific instructions to work well (LLMs).</summary>
    public bool RequiresAnalyzerInstructions { get; init; }

    /// <summary>True when the source naturally analyzes the whole repository in one pass (static tools).</summary>
    public bool SupportsRepositoryWideAnalysis { get; init; }

    public string? Version { get; init; }

    /// <summary>
    /// The provider's OWN configured per-execution timeout (Milestone 015.2B),
    /// when it has one. The acquisition executor uses this as the step timeout and
    /// falls back to <c>Evidence:Execution:ProviderTimeout</c> when it is null —
    /// so a provider's specific timeout is never overridden by the global default.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Whether this source can serve the given discipline.</summary>
    public bool Supports(FindingCategory discipline)
        => SupportedDisciplines.Count == 0 || SupportedDisciplines.Contains(discipline);
}
