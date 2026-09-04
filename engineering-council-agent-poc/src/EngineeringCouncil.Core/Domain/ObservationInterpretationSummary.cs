namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Telemetry for the evidence-interpretation stage: how much evidence was
/// processed, how many observations were produced, and where interpretation
/// failed or was unsupported.
/// </summary>
public sealed record ObservationInterpretationSummary
{
    /// <summary>Successful evidence items that were handed to an interpreter.</summary>
    public int EvidenceProcessed { get; init; }

    /// <summary>Evidence items that failed at acquisition (never interpreted).</summary>
    public int EvidenceFailed { get; init; }

    /// <summary>Successful evidence items with no interpreter able to handle them.</summary>
    public int EvidenceUnsupported { get; init; }

    public int ObservationsProduced { get; init; }

    /// <summary>
    /// Observations whose discipline is <see cref="FindingCategory.Unknown"/> — evidence
    /// no analyzer claims by design. Surfaced so unclaimed signal is visible in
    /// diagnostics instead of silently falling into another discipline.
    /// </summary>
    public int UnclaimedObservationCount { get; init; }

    /// <summary>Interpreters that threw while interpreting (isolated, not fatal).</summary>
    public int InterpreterFailures { get; init; }

    /// <summary>
    /// Observations from a discipline-scoped request whose discipline did not match
    /// the requested one (kept with reduced confidence per the documented rule).
    /// </summary>
    public int DisciplineMismatches { get; init; }

    /// <summary>
    /// File references a source claimed that do not exist in the scanned snapshot and
    /// were therefore dropped by the "do not invent files" guard. A calibration signal
    /// for prompt quality (Milestone 012); it never changes the produced observations.
    /// </summary>
    public int InvalidFileReferencesDropped { get; init; }

    public IReadOnlyDictionary<string, int> ObservationsByDiscipline { get; init; }
        = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ObservationsByProvider { get; init; }
        = new Dictionary<string, int>();

    public TimeSpan Duration { get; init; }
}
