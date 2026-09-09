namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Identifies a unique graph cache entry. A graph is reusable only when both
/// the repository path and the snapshot fingerprint match exactly.
/// </summary>
public readonly record struct GraphCacheKey
{
    /// <summary>Normalized repository root path (full, lowercase on case-insensitive OS).</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>Snapshot fingerprint from <see cref="Analysis.SnapshotFingerprint.Compute"/>.</summary>
    public required string SnapshotFingerprint { get; init; }

    /// <summary>Cache directory name derived from the key.</summary>
    public string CacheDirName => $"{SanitizePath(RepositoryPath)}_{SnapshotFingerprint}";

    private static string SanitizePath(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Create(path.Length, path, (span, state) =>
        {
            for (int i = 0; i < state.Length; i++)
            {
                char c = state[i];
                span[i] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
            }
        });
    }
}

/// <summary>
/// Metadata stored alongside a cached graph.json to validate that the graph
/// matches the current repository snapshot.
/// </summary>
public sealed record GraphArtifact
{
    /// <summary>The snapshot fingerprint this graph was built from.</summary>
    public required string SnapshotFingerprint { get; init; }

    /// <summary>Repository root path used during extraction.</summary>
    public required string RepositoryPath { get; init; }

    /// <summary>Absolute path to the graph.json file.</summary>
    public required string GraphJsonPath { get; init; }

    /// <summary>UTC timestamp when the graph was extracted.</summary>
    public DateTimeOffset ExtractedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Graphify CLI version used for extraction, if known.</summary>
    public string? GraphifyVersion { get; init; }

    /// <summary>Number of nodes in the extracted graph.</summary>
    public int NodeCount { get; init; }

    /// <summary>Number of edges (links) in the extracted graph.</summary>
    public int EdgeCount { get; init; }
}
