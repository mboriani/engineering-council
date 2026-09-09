namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for graph-assisted agentic context (M16.3). When enabled,
/// a persisted Graphify graph provides discipline-specific navigation context
/// to agentic providers via <see cref="EvidenceRequest.AdditionalContext"/>.
/// Graph assistance is opt-in, non-blocking, and falls back silently on failure.
/// </summary>
public sealed class GraphAssistanceOptions
{
    public const string SectionName = "GraphAssistance";

    /// <summary>Enable graph-assisted context generation. Default false (disabled).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Root directory for cached graph artifacts. Each repository snapshot gets
    /// a subdirectory keyed by its snapshot fingerprint.
    /// </summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".engineering-council", "graph-cache");

    /// <summary>
    /// The Graphify executable name or path. Default "graphify".
    /// Only used on cache miss to extract a new graph.
    /// </summary>
    public string GraphifyExecutable { get; set; } = "graphify";

    /// <summary>
    /// Per-extraction timeout for the Graphify process. Default 120 seconds.
    /// </summary>
    public int ExtractionTimeoutSeconds { get; set; } = 120;
}
