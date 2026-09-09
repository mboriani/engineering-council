using System.Diagnostics;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Experiments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.GraphAssistance;

/// <summary>
/// Orchestrates graph acquisition (cache hit or Graphify extraction), then
/// generates discipline-specific navigation context from the shared graph.
/// The graph is loaded at most once per repository snapshot; different
/// disciplines receive different views of the same graph.
/// </summary>
public sealed class GraphContextProvider : IGraphContextProvider
{
    private readonly GraphAssistanceOptions _options;
    private readonly GraphCache _cache;
    private readonly GraphifyCliRunner _runner;
    private readonly ILogger _logger;

    /// <summary>Supported disciplines and their context builders.</summary>
    private static readonly IReadOnlyDictionary<FindingCategory, Func<string, string>> ContextBuilders
        = new Dictionary<FindingCategory, Func<string, string>>
        {
            [FindingCategory.Security] = BuildSecurityContext,
            [FindingCategory.Architecture] = BuildArchitectureContext,
        };

    public GraphContextProvider(
        GraphAssistanceOptions options,
        GraphCache cache,
        GraphifyCliRunner runner,
        ILogger? logger = null)
    {
        _options = options;
        _cache = cache;
        _runner = runner;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<GraphContextResult> GetContextAsync(
        string repositoryPath,
        string snapshotFingerprint,
        FindingCategory discipline,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return new GraphContextResult { Enabled = false };

        if (!ContextBuilders.ContainsKey(discipline))
        {
            _logger.LogDebug("Graph assistance not available for discipline {Discipline}", discipline);
            return new GraphContextResult { Enabled = true };
        }

        var key = new GraphCacheKey
        {
            RepositoryPath = Path.GetFullPath(repositoryPath),
            SnapshotFingerprint = snapshotFingerprint
        };

        var extractionExecuted = false;
        TimeSpan? extractionDuration = null;

        // Synchronize: only one extraction per snapshot at a time
        await _cache.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var artifact = _cache.TryGet(key);

            if (artifact is null)
            {
                // Cache miss — extract graph
                extractionExecuted = true;
                var sw = Stopwatch.StartNew();
                var graphPath = Path.Combine(
                    _options.CacheDirectory,
                    key.CacheDirName,
                    "graph.json");

                var success = await _runner.ExtractAsync(
                    repositoryPath, graphPath, cancellationToken).ConfigureAwait(false);

                if (!success)
                    return new GraphContextResult
                    {
                        Enabled = true,
                        ExtractionExecuted = true,
                        FailureReason = "Graphify extraction failed"
                    };

                sw.Stop();
                extractionDuration = sw.Elapsed;

                artifact = new GraphArtifact
                {
                    SnapshotFingerprint = snapshotFingerprint,
                    RepositoryPath = key.RepositoryPath,
                    GraphJsonPath = graphPath,
                    ExtractedAt = DateTimeOffset.UtcNow
                };

                // Read node/edge counts from graph
                try
                {
                    var graphJson = File.ReadAllText(graphPath);
                    using var doc = JsonDocument.Parse(graphJson);
                    if (doc.RootElement.TryGetProperty("nodes", out var nodes))
                        artifact = artifact with { NodeCount = nodes.GetArrayLength() };
                    if (doc.RootElement.TryGetProperty("links", out var links))
                        artifact = artifact with { EdgeCount = links.GetArrayLength() };
                }
                catch { /* best effort — counts are informational */ }

                _cache.Store(key, artifact);
            }
            else
            {
                _logger.LogDebug("Graph cache hit for {Snapshot}", snapshotFingerprint);
            }

            // Build context from graph
            var loadSw = Stopwatch.StartNew();
            string graphJsonContent;
            try
            {
                graphJsonContent = File.ReadAllText(artifact.GraphJsonPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cached graph: {Path}", artifact.GraphJsonPath);
                return new GraphContextResult
                {
                    Enabled = true,
                    CacheHit = !extractionExecuted,
                    FailureReason = "Graph file unreadable"
                };
            }
            loadSw.Stop();

            var selectSw = Stopwatch.StartNew();
            var builder = ContextBuilders[discipline];
            var context = builder(graphJsonContent);
            selectSw.Stop();

            return new GraphContextResult
            {
                Enabled = true,
                Context = context,
                CacheHit = !extractionExecuted,
                ExtractionExecuted = extractionExecuted,
                ExtractionDuration = extractionDuration,
                GraphLoadDuration = loadSw.Elapsed,
                ContextSelectionDuration = selectSw.Elapsed,
                ContextCharacters = context?.Length ?? 0,
                Discipline = discipline
            };
        }
        finally
        {
            _cache.Lock.Release();
        }
    }

    private static string BuildSecurityContext(string graphJson)
    {
        var graph = JsonSerializer.Deserialize<GraphifySecurityContextBuilder.GraphifyGraph>(
            graphJson, GraphifySecurityContextBuilder.JsonOptions);
        if (graph is null) return string.Empty;
        var builder = new GraphifySecurityContextBuilder(GraphContextOptions.MinimalNavigation);
        return builder.Build(graph);
    }

    private static string BuildArchitectureContext(string graphJson)
    {
        var graph = JsonSerializer.Deserialize<GraphifyArchitectureContextBuilder.GraphifyGraph>(
            graphJson, GraphifyArchitectureContextBuilder.JsonOptions);
        if (graph is null) return string.Empty;
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);
        return builder.Build(graph);
    }
}
