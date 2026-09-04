namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Deterministic counts of the four <see cref="ReconciliationAssessmentType"/> values
/// across a run's consolidated findings (Milestone 014.3). A PURE AGGREGATION of the
/// already-computed <see cref="Finding.CouncilAssessment"/> — never a new
/// classification, never a recomputation. There is exactly one authoritative source
/// for these counts (<see cref="Application.CouncilAssessmentBuilder.Summarize"/>);
/// no exporter derives them independently.
/// </summary>
public sealed record CouncilAssessmentSummary
{
    public required int SingleSourceCount { get; init; }
    public required int StrongAgreementCount { get; init; }
    public required int AgreementWithDifferencesCount { get; init; }
    public required int PotentialConflictCount { get; init; }

    /// <summary>Sum of the four counts — consolidated findings that carry a Council assessment.</summary>
    public int TotalAssessed
        => SingleSourceCount + StrongAgreementCount + AgreementWithDifferencesCount + PotentialConflictCount;
}
