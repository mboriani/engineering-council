namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The diagnostic kind emitted in <see cref="CalibrationDiagnosticsReport"/>.
/// M12.3 emits exactly three: findings reported by only one compared provider
/// (<see cref="ExclusiveFinding"/>), shared findings whose reconciled sources
/// disagreed on severity (<see cref="SeverityDisagreement"/>), and disciplines
/// where the compared providers agreed on few findings given a sufficient sample
/// (<see cref="LowAgreement"/>). M12.4 adds exactly two, both on shared findings
/// in Comparable disciplines: supporting observations whose normalized
/// <see cref="EngineeringObservation.ObservationType"/> differs by provider
/// (<see cref="ObservationTypeDisagreement"/>), and supporting observations that
/// reference meaningfully different normalized source locations
/// (<see cref="LocationDisagreement"/>).
/// </summary>
public enum CalibrationDiagnosticType
{
    ExclusiveFinding,
    SeverityDisagreement,
    LowAgreement,
    ObservationTypeDisagreement,
    LocationDisagreement
}

/// <summary>
/// Deterministic sample-size / rate thresholds for the <see cref="CalibrationDiagnosticType.LowAgreement"/>.
/// Descriptive only — no ranking, scoring, or weighting of providers.
/// </summary>
public sealed record CalibrationDiagnosticCriteria
{
    /// <summary>Minimum consolidated findings in a Comparable discipline before a LowAgreement signal is emitted.</summary>
    public int MinimumSampleFindings { get; init; } = 3;

    /// <summary>Agreement rate strictly below this emits a LowAgreement signal (shared ÷ consolidated).</summary>
    public double LowAgreementThreshold { get; init; } = 0.5;
}

/// <summary>One provider/severity pair, used for severity-disagreement attribution.</summary>
public sealed record ProviderSeverity
{
    public string Provider { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
}

/// <summary>One provider/observation-type set, used for observation-type-disagreement attribution.</summary>
public sealed record ProviderObservationType
{
    public string Provider { get; init; } = string.Empty;

    /// <summary>Distinct normalized observation types attributed to this provider (sorted).</summary>
    public IReadOnlyList<string> ObservationTypes { get; init; } = [];
}

/// <summary>One provider/normalized-location set, used for location-disagreement attribution.</summary>
public sealed record ProviderLocation
{
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Distinct normalized source locations attributed to this provider (sorted).
    /// A location is the normalized repository-relative path, optionally with an
    /// explicit 1-based line (<c>src/payments/paymentclient.cs:42</c>).
    /// </summary>
    public IReadOnlyList<string> NormalizedLocations { get; init; } = [];
}

/// <summary>
/// One calibration diagnostic. Fields are populated per <see cref="Type"/>:
/// <see cref="ExclusiveFinding"/> fills Provider/FindingId/Title/Severity/Confidence/Files;
/// <see cref="SeverityDisagreement"/> fills FindingId/Title/SeverityRange/SeverityRationale/ProviderSeverities;
/// <see cref="LowAgreement"/> fills ConsolidatedFindingCount/SharedFindingCount/AgreementRate/
/// ExclusiveFindingCountByProvider; <see cref="ObservationTypeDisagreement"/> fills
/// FindingId/Title/Providers/ObservationIds/ObservationTypesByProvider;
/// <see cref="LocationDisagreement"/> fills FindingId/Title/Providers/ObservationIds/
/// LocationsByProvider.
/// </summary>
public sealed record FindingDiagnostic
{
    public CalibrationDiagnosticType Type { get; init; }
    public FindingCategory Discipline { get; init; }

    public string Provider { get; init; } = string.Empty;
    public string FindingId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public FindingSeverity Severity { get; init; } = FindingSeverity.Medium;
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Medium;
    public IReadOnlyList<string> Files { get; init; } = [];

    // SeverityDisagreement only.
    public string SeverityRange { get; init; } = string.Empty;
    public string SeverityRationale { get; init; } = string.Empty;
    public IReadOnlyList<ProviderSeverity> ProviderSeverities { get; init; } = [];

    // ObservationTypeDisagreement / LocationDisagreement only (M12.4).
    public IReadOnlyList<string> Providers { get; init; } = [];
    public IReadOnlyList<string> ObservationIds { get; init; } = [];
    public IReadOnlyList<ProviderObservationType> ObservationTypesByProvider { get; init; } = [];
    public IReadOnlyList<ProviderLocation> LocationsByProvider { get; init; } = [];

    // LowAgreement only.
    public int ConsolidatedFindingCount { get; init; }
    public int SharedFindingCount { get; init; }
    public double? AgreementRate { get; init; }
    public IReadOnlyList<NamedCount> ExclusiveFindingCountByProvider { get; init; } = [];
}

/// <summary>
/// The INTERNAL calibration-diagnostics artifact (M12.3, extended additively by
/// M12.4). A deterministic projection of a completed run's
/// <see cref="ProviderComparisonReport"/> and reconciled
/// <see cref="AnalysisRun.Findings"/> — no LLM calls, no rescan, no reinterpretation,
/// no semantic matching, and no ranking/scoring of providers. The existing
/// reconciliation is authoritative. Never part of the external
/// <c>engineering-review-package.json</c> contract.
/// </summary>
public sealed record CalibrationDiagnosticsReport
{
    public required string RunId { get; init; }

    /// <summary>Repository identity of the run these diagnostics describe.</summary>
    public string Repository { get; init; } = string.Empty;
    public string RepositoryBranch { get; init; } = string.Empty;
    public string RepositoryCommit { get; init; } = string.Empty;

    /// <summary>Only Comparable disciplines contribute diagnostics; all are deterministic and sorted.</summary>
    public IReadOnlyList<FindingDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>NonComparable/Incomplete disciplines are recorded here as limitations only — never as diagnostics.</summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];

    public CalibrationDiagnosticCriteria Criteria { get; init; } = new();

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
