using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Interpretation;

/// <summary>
/// Runs interpretation over a collection of evidence: resolves an interpreter per
/// item, interprets successful evidence, preserves failed acquisitions for
/// telemetry, isolates interpreter failures, and returns a unified observation
/// collection with a summary. Sequential; the per-item work is isolated so a
/// switch to parallel interpretation is a localized change.
/// </summary>
public sealed class EvidenceInterpretationPipeline : IEvidenceInterpretationPipeline
{
    private readonly IEvidenceInterpreterResolver _resolver;
    private readonly ILogger<EvidenceInterpretationPipeline> _logger;

    public EvidenceInterpretationPipeline(
        IEvidenceInterpreterResolver resolver,
        ILogger<EvidenceInterpretationPipeline>? logger = null)
    {
        _resolver = resolver;
        _logger = logger ?? NullLogger<EvidenceInterpretationPipeline>.Instance;
    }

    public async Task<InterpretationResult> InterpretAsync(
        IReadOnlyList<Core.Domain.Evidence> evidence, AnalyzerContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<EngineeringObservation>();
        int processed = 0, failed = 0, unsupported = 0, interpreterFailures = 0;

        foreach (var item in evidence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.Success)
            {
                failed++;
                continue; // preserved in provider telemetry; nothing to interpret
            }

            var interpreter = _resolver.Resolve(item);
            if (interpreter is null)
            {
                unsupported++;
                _logger.LogWarning("No interpreter for evidence from '{Provider}' ({Type})",
                    item.ProviderName, item.ProviderType);
                continue; // never invent observations for unsupported evidence
            }

            try
            {
                var produced = await interpreter.InterpretAsync(item, context, cancellationToken).ConfigureAwait(false);
                observations.AddRange(produced);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                interpreterFailures++;
                _logger.LogError(ex, "Interpreter '{Interpreter}' failed on evidence from '{Provider}'",
                    interpreter.ProviderName, item.ProviderName);
            }
        }

        // Assign stable, unique observation ids across the whole run.
        var numbered = observations
            .Select((o, i) => o with { Id = $"OBS-{i + 1:D3}" })
            .ToList();

        stopwatch.Stop();

        var summary = new ObservationInterpretationSummary
        {
            EvidenceProcessed = processed,
            EvidenceFailed = failed,
            EvidenceUnsupported = unsupported,
            ObservationsProduced = numbered.Count,
            InterpreterFailures = interpreterFailures,
            UnclaimedObservationCount = numbered.Count(o => o.Discipline == FindingCategory.Unknown),
            DisciplineMismatches = numbered.Count(o =>
                o.AcquisitionScope == EvidenceAcquisitionScope.Discipline
                && o.RequestedDiscipline is { } r && o.Discipline != r),
            InvalidFileReferencesDropped = numbered.Sum(o =>
                o.Metadata.TryGetValue("droppedFileReferences", out var dropped)
                && int.TryParse(dropped, out var count) ? count : 0),
            ObservationsByDiscipline = numbered
                .GroupBy(o => o.Discipline.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            ObservationsByProvider = numbered
                .GroupBy(o => o.SourceProvider)
                .ToDictionary(g => g.Key, g => g.Count()),
            Duration = stopwatch.Elapsed
        };

        _logger.LogInformation(
            "Interpreted {Processed} evidence item(s) into {Observations} observation(s) ({Unsupported} unsupported, {Failures} interpreter failures)",
            processed, numbered.Count, unsupported, interpreterFailures);

        return new InterpretationResult { Observations = numbered, Summary = summary };
    }
}
