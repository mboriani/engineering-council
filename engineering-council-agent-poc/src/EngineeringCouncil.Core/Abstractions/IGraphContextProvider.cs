using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Provides discipline-specific graph navigation context for agentic providers.
/// The graph is acquired/reused once per repository snapshot, then different
/// discipline views are generated from the same graph object.
/// </summary>
public interface IGraphContextProvider
{
    /// <summary>
    /// Gets the graph-assisted navigation context for the given discipline,
    /// or null if graph assistance is unavailable, disabled, or the discipline
    /// is not supported.
    /// </summary>
    Task<GraphContextResult> GetContextAsync(
        string repositoryPath,
        string snapshotFingerprint,
        FindingCategory discipline,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of graph context generation, including the context text and
/// diagnostics for telemetry.
/// </summary>
public sealed record GraphContextResult
{
    /// <summary>The generated navigation context, or null if unavailable.</summary>
    public string? Context { get; init; }

    /// <summary>True if the graph was served from cache (no extraction).</summary>
    public bool CacheHit { get; init; }

    /// <summary>True if Graphify extraction was executed.</summary>
    public bool ExtractionExecuted { get; init; }

    /// <summary>Duration of Graphify extraction if executed, otherwise null.</summary>
    public TimeSpan? ExtractionDuration { get; init; }

    /// <summary>Duration of graph JSON loading/deserialization.</summary>
    public TimeSpan? GraphLoadDuration { get; init; }

    /// <summary>Duration of discipline-specific context selection.</summary>
    public TimeSpan? ContextSelectionDuration { get; init; }

    /// <summary>Character count of the generated context.</summary>
    public int ContextCharacters { get; init; }

    /// <summary>The discipline this context was generated for.</summary>
    public FindingCategory Discipline { get; init; }

    /// <summary>Error reason if graph assistance failed, otherwise null.</summary>
    public string? FailureReason { get; init; }

    /// <summary>True if graph assistance was disabled.</summary>
    public bool Enabled { get; init; }
}
