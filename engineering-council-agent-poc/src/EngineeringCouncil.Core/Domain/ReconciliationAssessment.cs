namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Deterministic Council assessment of a consolidated finding (Milestone 014.2).
/// A pure, provider-neutral classification of HOW STRONGLY independent providers
/// agree on the finding, computed ONLY from existing authoritative data — the
/// reconciler's own fields on the consolidated <see cref="Finding"/>
/// (<c>SupportingProviders</c>, <c>SeverityRange</c>, <c>ContradictionReasons</c>)
/// and the M12.3/M12.4 <see cref="CalibrationDiagnosticsReport"/> when present.
/// No LLM, no re-reconciliation, no voting, no provider weighting.
/// </summary>
public enum ReconciliationAssessmentType
{
    /// <summary>Exactly one distinct supporting provider. Confidence/severity are never discounted for being single-source.</summary>
    SingleSource,

    /// <summary>At least two distinct supporting providers and no known reconciliation disagreements.</summary>
    StrongAgreement,

    /// <summary>
    /// At least two distinct supporting providers and existing reconciliation/diagnostic
    /// data reports a severity, observation-type, or location disagreement. Still agreement
    /// on the underlying finding — a difference is NOT a conflict.
    /// </summary>
    AgreementWithDifferences,

    /// <summary>
    /// Existing data EXPLICITLY demonstrates a contradiction. Never inferred from different
    /// severity, different location, different observation type, or a provider not reporting
    /// the finding. Zero of these is a valid outcome.
    /// </summary>
    PotentialConflict
}

/// <summary>Known, non-conflicting reconciliation difference kinds the assessment can surface.</summary>
public enum ReconciliationAssessmentDisagreement
{
    Severity,
    ObservationType,
    Location
}

/// <summary>
/// The deterministic Council assessment attached to a consolidated finding. Additively
/// serialized as the finding's <c>councilAssessment</c> (see the consumer DTO gate in
/// <c>PackageContractTests</c>). All collections are provider-neutral and sorted.
/// </summary>
public sealed record ReconciliationAssessment
{
    public required ReconciliationAssessmentType Type { get; init; }

    /// <summary>Distinct supporting providers, sorted (mirrors the reconciler's own counting).</summary>
    public required IReadOnlyList<string> SupportingProviders { get; init; }

    /// <summary>Number of distinct independent providers supporting the finding.</summary>
    public required int AgreementCount { get; init; }

    /// <summary>Difference kinds reported by existing data (Severity / ObservationType / Location), sorted.</summary>
    public IReadOnlyList<ReconciliationAssessmentDisagreement> Differences { get; init; } = [];

    /// <summary>Explicit contradiction reasons when <see cref="Type"/> is <see cref="ReconciliationAssessmentType.PotentialConflict"/>.</summary>
    public IReadOnlyList<string> ContradictionReasons { get; init; } = [];
}