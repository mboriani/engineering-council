using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.GraphAssistance;

/// <summary>
/// Manages persisted Graphify graph artifacts. Each graph is keyed by
/// repository path + snapshot fingerprint. On cache hit, the existing
/// graph.json is reused without re-extraction. Snapshot validation prevents
/// stale graph reuse.
/// </summary>
public sealed class GraphCache
{
    private readonly GraphAssistanceOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, GraphArtifact> _inMemory = new(StringComparer.OrdinalIgnoreCase);

    public GraphCache(GraphAssistanceOptions options, ILogger? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger.Instance;
        Directory.CreateDirectory(_options.CacheDirectory);
    }

    /// <summary>
    /// Attempts to retrieve a cached graph for the given snapshot.
    /// Returns null on cache miss or if the cached graph's snapshot does not match.
    /// </summary>
    public GraphArtifact? TryGet(GraphCacheKey key)
    {
        var cacheDir = GetCacheDir(key);

        // Check in-memory cache first
        if (_inMemory.TryGetValue(cacheDir, out var cached) && File.Exists(cached.GraphJsonPath))
        {
            if (string.Equals(cached.SnapshotFingerprint, key.SnapshotFingerprint, StringComparison.Ordinal))
            {
                _logger.LogDebug("Graph cache hit (in-memory): {CacheDir}", cacheDir);
                return cached;
            }

            _logger.LogWarning("Graph cache stale (in-memory): cached={Cached} current={Current}",
                cached.SnapshotFingerprint, key.SnapshotFingerprint);
            return null;
        }

        // Check disk
        var metadataPath = Path.Combine(cacheDir, "graph-metadata.json");
        if (!File.Exists(metadataPath))
        {
            _logger.LogDebug("Graph cache miss: {CacheDir}", cacheDir);
            return null;
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            var artifact = JsonSerializer.Deserialize<GraphArtifact>(json, GraphJsonOptions);
            if (artifact is null || !File.Exists(artifact.GraphJsonPath))
            {
                _logger.LogDebug("Graph cache invalid metadata: {CacheDir}", cacheDir);
                return null;
            }

            if (!string.Equals(artifact.SnapshotFingerprint, key.SnapshotFingerprint, StringComparison.Ordinal))
            {
                _logger.LogWarning("Graph cache stale on disk: cached={Cached} current={Current}",
                    artifact.SnapshotFingerprint, key.SnapshotFingerprint);
                return null;
            }

            _inMemory[cacheDir] = artifact;
            _logger.LogDebug("Graph cache hit (disk): {CacheDir}", cacheDir);
            return artifact;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph cache read error: {CacheDir}", cacheDir);
            return null;
        }
    }

    /// <summary>
    /// Stores a graph artifact in the cache. Creates the cache directory
    /// and writes metadata. The in-memory cache is updated atomically.
    /// </summary>
    public void Store(GraphCacheKey key, GraphArtifact artifact)
    {
        var cacheDir = GetCacheDir(key);
        Directory.CreateDirectory(cacheDir);

        var metadataPath = Path.Combine(cacheDir, "graph-metadata.json");
        var json = JsonSerializer.Serialize(artifact, GraphJsonOptions);
        File.WriteAllText(metadataPath, json);

        _inMemory[cacheDir] = artifact;
        _logger.LogDebug("Graph cached: {CacheDir} (nodes={Nodes}, edges={Edges})",
            cacheDir, artifact.NodeCount, artifact.EdgeCount);
    }

    /// <summary>
    /// Acquires the process-local lock for graph extraction.
    /// Ensures only one Graphify extraction runs at a time for a given process.
    /// </summary>
    public SemaphoreSlim Lock => _lock;

    private string GetCacheDir(GraphCacheKey key)
        => Path.Combine(_options.CacheDirectory, key.CacheDirName);

    private static readonly JsonSerializerOptions GraphJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
