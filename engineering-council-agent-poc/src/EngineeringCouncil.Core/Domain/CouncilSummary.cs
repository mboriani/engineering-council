namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Statistics about the consolidation the <c>IFindingMerger</c> performed.
/// </summary>
public sealed record MergeSummary
{
    /// <summary>Number of findings produced by the analyzers before merging.</summary>
    public int RawFindingCount { get; init; }

    /// <summary>Number of findings after consolidation.</summary>
    public int ConsolidatedFindingCount { get; init; }

    /// <summary>How many consolidated findings resulted from merging two or more raw findings.</summary>
    public int MergedGroupCount { get; init; }

    /// <summary>How many raw findings were folded away by merging (raw − consolidated).</summary>
    public int DuplicatesRemoved { get; init; }

    /// <summary>Human-readable one-liner.</summary>
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// The executive, council-level view over a run's consolidated findings.
/// Rule-based in Milestone 003 (no LLM); the shape is ready for an LLM-authored
/// summary later.
/// </summary>
public sealed record CouncilSummary
{
    public string ExecutiveSummary { get; init; } = string.Empty;

    public IReadOnlyList<string> KeyRisks { get; init; } = [];

    public IReadOnlyList<string> RecommendedNextActions { get; init; } = [];

    /// <summary>Consolidated finding counts by category (keys are category names).</summary>
    public IReadOnlyDictionary<string, int> CategoryBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Consolidated finding counts by severity (keys are severity names).</summary>
    public IReadOnlyDictionary<string, int> SeverityBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Consolidated finding counts by confidence (keys are confidence names).</summary>
    public IReadOnlyDictionary<string, int> ConfidenceBreakdown { get; init; }
        = new Dictionary<string, int>();

    public MergeSummary MergeSummary { get; init; } = new();

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
