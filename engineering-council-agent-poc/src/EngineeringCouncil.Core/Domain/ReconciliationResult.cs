namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// One deterministic reconciliation cluster: the raw findings judged to describe
/// the same engineering issue, plus why and how strongly they matched. This is
/// INTERNAL traceability — the external application consumes the consolidated
/// <see cref="Finding"/>s, not these groups (they ride in the package appendix
/// generically).
/// </summary>
public sealed record ReconciliationGroup
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> SourceFindingIds { get; init; }
    public required IReadOnlyList<string> ObservationIds { get; init; }
    public required IReadOnlyList<string> SupportingProviders { get; init; }
    public required string ReconciliationReason { get; init; }

    /// <summary>Deterministic match strength in [0,1] (1.0 = exact rule+location; lower = weaker signal).</summary>
    public required double MatchScore { get; init; }

    public required bool WasConsolidated { get; init; }
    public string? ConsolidatedFindingId { get; init; }
}

/// <summary>Provider-neutral summary of a reconciliation pass. Part of the package.</summary>
public sealed record ReconciliationSummary
{
    public required int RawFindingCount { get; init; }
    public required int ConsolidatedFindingCount { get; init; }
    public required int ReconciliationGroupCount { get; init; }
    public required int MultiProviderFindingCount { get; init; }
    public required int SingleProviderFindingCount { get; init; }
    public required int ContradictionCount { get; init; }

    // Deterministic dedup diagnostics (Milestone 15.3D) — additive to the existing
    // reconciliation contract. Each dedup-identity join removes exactly one finding,
    // so PreDedup = PostDedup + Deduplicated. Defaulted (not required) so persisted
    // pre-M15.3D documents still deserialize with these fields reading zero.
    /// <summary>Consolidated findings that would exist WITHOUT the deterministic dedup stage (M15.3D).</summary>
    public int PreDedupFindingCount { get; init; }
    /// <summary>Consolidated findings AFTER the deterministic dedup stage (equals <see cref="ConsolidatedFindingCount"/>).</summary>
    public int PostDedupFindingCount { get; init; }
    /// <summary>Findings removed by the deterministic dedup stage (PreDedup − PostDedup).</summary>
    public int DeduplicatedFindingCount { get; init; }

    /// <summary>Raw findings attributed to each provider (a finding backed by N providers counts once per provider).</summary>
    public required IReadOnlyDictionary<string, int> FindingsByProvider { get; init; }

    public required TimeSpan Duration { get; init; }
}

/// <summary>The result of a deterministic reconciliation pass.</summary>
public sealed record ReconciliationResult
{
    public required IReadOnlyList<Finding> ConsolidatedFindings { get; init; }
    public required IReadOnlyList<ReconciliationGroup> Groups { get; init; }
    public required ReconciliationSummary Summary { get; init; }
}
