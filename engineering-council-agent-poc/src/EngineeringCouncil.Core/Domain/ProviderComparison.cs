namespace EngineeringCouncil.Core.Domain;

/// <summary>A name + count pair used for deterministic distribution tables (types, severities, confidences, providers).</summary>
public sealed record NamedCount
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// State of a single discipline comparison. <see cref="Comparable"/> = at least two
/// providers executed the discipline against the same effective context and all
/// succeeded. <see cref="NonComparable"/> = providers executed the same discipline
/// but received different effective contexts (fingerprints differ) — execution
/// metrics are still reported, agreement is not. <see cref="Incomplete"/> = at least
/// one provider execution failed, so meaningful output comparison is unavailable.
/// </summary>
public enum ProviderComparisonStatus
{
    Comparable,
    NonComparable,
    Incomplete
}

/// <summary>
/// Provider-neutral, deterministic comparison of multiple LLM providers over the
/// SAME repository/disciplines from data already produced by the review pipeline
/// (Milestone 012.2). Descriptive only — never ranking, scoring, weighting, or
/// calibration. This is an INTERNAL evaluation artifact, not part of the external
/// <c>engineering-review-package.json</c> consumer contract.
/// </summary>
public sealed record ProviderComparisonReport
{
    public required string RunId { get; init; }

    /// <summary>Repository identity (solution name / branch / commit) of the compared run.</summary>
    public string Repository { get; init; } = string.Empty;
    public string RepositoryBranch { get; init; } = string.Empty;
    public string RepositoryCommit { get; init; } = string.Empty;

    /// <summary>The distinct LLM providers compared (any provider with an execution in a compared discipline).</summary>
    public IReadOnlyList<string> ComparedProviders { get; init; } = [];

    /// <summary>One comparison per discipline with at least two LLM provider executions.</summary>
    public IReadOnlyList<DisciplineComparison> DisciplineComparisons { get; init; } = [];

    /// <summary>
    /// Per-provider execution rollups over the compared LLM executions (reuses the
    /// M12.1 <see cref="ProviderExecutionSummary"/> aggregation — no re-collection).
    /// </summary>
    public IReadOnlyList<ProviderExecutionSummary> OverallExecutionMetrics { get; init; } = [];

    /// <summary>Deterministic, provider-neutral limitations/notes for reading this report.</summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Per-discipline provider comparison.</summary>
public sealed record DisciplineComparison
{
    public FindingCategory Discipline { get; init; }

    /// <summary>The compared provider names in this discipline (≥2).</summary>
    public IReadOnlyList<string> Providers { get; init; } = [];

    /// <summary>True when all compared executions share the same effective context fingerprint.</summary>
    public bool ComparableContext { get; init; }

    /// <summary>The shared context fingerprint when <see cref="ComparableContext"/>; empty otherwise.</summary>
    public string ContextFingerprint { get; init; } = string.Empty;

    /// <summary>Human description of the compared context (files, characters, fingerprint).</summary>
    public string ComparableContextDescription { get; init; } = string.Empty;

    public ProviderComparisonStatus Status { get; init; }

    /// <summary>Per-provider execution metrics, always present (including failed executions).</summary>
    public IReadOnlyList<ProviderExecutionComparison> ExecutionMetrics { get; init; } = [];

    /// <summary>Per-provider observation metrics; empty when output comparison is unavailable.</summary>
    public IReadOnlyList<ProviderObservationComparison> ObservationMetrics { get; init; } = [];

    /// <summary>Per-provider finding metrics; empty when output comparison is unavailable.</summary>
    public IReadOnlyList<ProviderFindingComparison> FindingMetrics { get; init; } = [];

    /// <summary>Agreement metrics; present only when the comparison is <see cref="ProviderComparisonStatus.Comparable"/>.</summary>
    public AgreementMetrics? Agreement { get; init; }
}

/// <summary>Per-provider execution metrics for one discipline (M12.1 telemetry reused verbatim).</summary>
public sealed record ProviderExecutionComparison
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    /// <summary>True when every execution for this provider in the discipline succeeded.</summary>
    public bool Success { get; init; }
    public int Executions { get; init; }
    public int Failures { get; init; }
    public TimeSpan Duration { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
    public int RetryCount { get; init; }
    public int RepairAttemptCount { get; init; }
    public bool? RepairSucceeded { get; init; }
    public string? ErrorCategory { get; init; }
    public bool ResponseTruncated { get; init; }
    public int ContextFileCount { get; init; }
    public int ContextCharacterCount { get; init; }
    public int ObservationCount { get; init; }
}

/// <summary>Per-provider normalized-observation metrics for one discipline (descriptive only).</summary>
public sealed record ProviderObservationComparison
{
    public string Provider { get; init; } = string.Empty;
    public int ObservationCount { get; init; }

    /// <summary>Normalized observation-type distribution (sorted by name).</summary>
    public IReadOnlyList<NamedCount> Types { get; init; } = [];

    /// <summary>Severity distribution (sorted by name).</summary>
    public IReadOnlyList<NamedCount> Severities { get; init; } = [];

    /// <summary>Confidence distribution (sorted by name).</summary>
    public IReadOnlyList<NamedCount> Confidences { get; init; } = [];

    /// <summary>Distinct files referenced by the provider's observations.</summary>
    public int FilesReferenced { get; init; }
    public int ObservationsWithLocation { get; init; }
    public int ObservationsWithoutLocation { get; init; }

    /// <summary>
    /// Files referenced ÷ context files supplied for the provider's execution.
    /// Null when the provider supplied no context files. Descriptive only — a high
    /// rate is not better quality.
    /// </summary>
    public double? ReferencedContextFileRate { get; init; }
}

/// <summary>Per-provider finding metrics for one discipline, derived from the existing reconciler output.</summary>
public sealed record ProviderFindingComparison
{
    public string Provider { get; init; } = string.Empty;
    public int RawFindingCount { get; init; }
    public int ConsolidatedFindingCount { get; init; }

    /// <summary>Consolidated findings supported ONLY by this provider among the compared providers.</summary>
    public int ExclusiveFindingCount { get; init; }
    public IReadOnlyList<string> ExclusiveFindingIds { get; init; } = [];

    /// <summary>Consolidated findings supported by ≥2 compared providers that this provider supports.</summary>
    public int MultiProviderFindingCount { get; init; }
}

/// <summary>
/// Descriptive agreement metrics for one discipline. Agreement is NOT correctness.
/// </summary>
public sealed record AgreementMetrics
{
    /// <summary>Consolidated findings in the discipline supported by ≥1 compared provider (the agreement denominator).</summary>
    public int ConsolidatedFindingCount { get; init; }

    /// <summary>Consolidated findings supported by ≥2 compared providers.</summary>
    public int SharedFindingCount { get; init; }

    /// <summary>Per-provider exclusive counts (sorted by name).</summary>
    public IReadOnlyList<NamedCount> ExclusiveFindingCountByProvider { get; init; } = [];

    /// <summary>Consolidated findings with <c>AgreementCount ≥ 2</c> per the reconciler (may include non-compared sources).</summary>
    public int MultiProviderFindingCount { get; init; }

    /// <summary>
    /// SharedFindingCount ÷ ConsolidatedFindingCount (shared consolidated findings
    /// over consolidated findings supported by any compared provider). Null when
    /// there are no consolidated findings. Never called "accuracy".
    /// </summary>
    public double? AgreementRate { get; init; }
}
