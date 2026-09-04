using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>Result of interpreting a collection of evidence.</summary>
public sealed record InterpretationResult
{
    public IReadOnlyList<EngineeringObservation> Observations { get; init; } = [];
    public required ObservationInterpretationSummary Summary { get; init; }
}

/// <summary>
/// Interprets the evidence collected by <see cref="IEvidenceExecutor"/> into a
/// unified observation collection. Preserves failed provider executions for
/// telemetry, isolates interpreter failures (one failure must not abort the
/// analysis), and reports unsupported evidence. Sequential in Milestone 007;
/// shaped so parallel interpretation can be introduced later.
/// </summary>
public interface IEvidenceInterpretationPipeline
{
    Task<InterpretationResult> InterpretAsync(
        IReadOnlyList<Evidence> evidence,
        AnalyzerContext context,
        CancellationToken cancellationToken = default);
}
