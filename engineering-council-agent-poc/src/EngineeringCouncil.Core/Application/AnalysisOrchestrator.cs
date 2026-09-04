using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Coordinates the specialized analyzer agents. It receives a
/// <see cref="RepositorySnapshot"/>, executes every registered
/// <see cref="IAnalyzerAgent"/>, aggregates their findings, and returns a single
/// list.
///
/// v1 executes analyzers sequentially. The seam is intentionally shaped so
/// parallel execution can be dropped in later (swap the loop for
/// <see cref="Task.WhenAll(System.Collections.Generic.IEnumerable{Task})"/>)
/// without changing callers or analyzers.
///
/// This is NOT a council: it does not merge, de-duplicate, or reconcile
/// findings — those belong to a future milestone.
/// </summary>
public sealed class AnalysisOrchestrator
{
    private readonly IReadOnlyList<IAnalyzerAgent> _analyzers;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        IEnumerable<IAnalyzerAgent> analyzers,
        ILogger<AnalysisOrchestrator>? logger = null)
    {
        _analyzers = analyzers.ToList();
        _logger = logger ?? NullLogger<AnalysisOrchestrator>.Instance;
    }

    /// <summary>The analyzers that will run, in registration order.</summary>
    public IReadOnlyList<string> AnalyzerNames => _analyzers.Select(a => a.Name).ToList();

    /// <summary>The distinct disciplines the registered analyzers cover.</summary>
    public IReadOnlyList<FindingCategory> Disciplines
        => _analyzers.Select(a => a.Category).Distinct().ToList();

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(
        IReadOnlyList<EngineeringObservation> observations,
        AnalyzerContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running {Count} specialized analyzer(s) over {Obs} observation(s): {Names}",
            _analyzers.Count, observations.Count, string.Join(", ", AnalyzerNames));

        var aggregated = new List<Finding>();

        // Sequential in v1. Keep each analyzer isolated: one failing analyzer
        // must not sink the whole run.
        foreach (var analyzer in _analyzers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var findings = await analyzer.AnalyzeAsync(observations, context, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "Analyzer '{Analyzer}' produced {Count} finding(s)", analyzer.Name, findings.Count);
                aggregated.AddRange(findings);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Analyzer '{Analyzer}' failed; skipping it", analyzer.Name);
            }
        }

        // Assign globally-unique raw ids across all analyzers. Each analyzer
        // numbers its own findings locally (F-001…), so ids collide once
        // aggregated; the consolidation layer needs unique ids to reference.
        return aggregated
            .Select((f, i) => f with { Id = $"RAW-{i + 1:D3}" })
            .ToList();
    }
}
