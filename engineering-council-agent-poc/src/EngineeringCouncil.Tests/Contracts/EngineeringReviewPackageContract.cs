using System.Text.Json.Serialization;

namespace EngineeringCouncil.Tests.Contracts;

// A consumer-facing DTO representing exactly what the EXTERNAL Engineering Review
// application relies on from engineering-review-package.json. It intentionally
// mirrors only the stable integration contract (a subset of the full package) and
// uses plain strings for enums to assert stable, string-serialized enum values.
// If a change breaks this DTO's deserialization, it breaks the external consumer.

public sealed record PackageContract
{
    [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("repository")] public string? Repository { get; init; }
    [JsonPropertyName("branch")] public string? Branch { get; init; }
    [JsonPropertyName("commit")] public string? Commit { get; init; }
    [JsonPropertyName("repositorySnapshot")] public RepositorySnapshotContract? RepositorySnapshot { get; init; }
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; init; }
    [JsonPropertyName("analysisRunId")] public string? AnalysisRunId { get; init; }
    [JsonPropertyName("executiveSummary")] public string? ExecutiveSummary { get; init; }
    [JsonPropertyName("overallEngineeringHealth")] public string? OverallEngineeringHealth { get; init; }
    [JsonPropertyName("overallRisk")] public string? OverallRisk { get; init; }

    [JsonPropertyName("findings")] public List<FindingContract> Findings { get; init; } = [];
    [JsonPropertyName("reconciliation")] public ReconciliationContract? Reconciliation { get; init; }
    [JsonPropertyName("councilAssessmentSummary")] public CouncilAssessmentSummaryContract? CouncilAssessmentSummary { get; init; }
    [JsonPropertyName("acquisitionCoverage")] public AcquisitionCoverageContract? AcquisitionCoverage { get; init; }
    [JsonPropertyName("disciplineCoverage")] public DisciplineCoverageContract? DisciplineCoverage { get; init; }
    [JsonPropertyName("staticAnalysisSources")] public List<StaticSourceContract> StaticAnalysisSources { get; init; } = [];
    [JsonPropertyName("metrics")] public MetricsContract? Metrics { get; init; }
}

public sealed record FindingContract
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("severity")] public string? Severity { get; init; }
    [JsonPropertyName("confidence")] public string? Confidence { get; init; }
    [JsonPropertyName("recommendation")] public string? Recommendation { get; init; }
    [JsonPropertyName("supportingProviders")] public List<string> SupportingProviders { get; init; } = [];
    [JsonPropertyName("agreementCount")] public int AgreementCount { get; init; }
    [JsonPropertyName("isConsolidated")] public bool IsConsolidated { get; init; }
    [JsonPropertyName("severityRange")] public string? SeverityRange { get; init; }
    [JsonPropertyName("reconciliationReason")] public string? ReconciliationReason { get; init; }
    [JsonPropertyName("observationIds")] public List<string> ObservationIds { get; init; } = [];
    [JsonPropertyName("sourceRules")] public List<string> SourceRules { get; init; } = [];
    [JsonPropertyName("supportingFindingIds")] public List<string> SupportingFindingIds { get; init; } = [];
    [JsonPropertyName("councilAssessment")] public CouncilAssessmentContract? CouncilAssessment { get; init; }
    [JsonPropertyName("semanticReview")] public SemanticReviewContract? SemanticReview { get; init; }
}

/// <summary>
/// Targeted semantic review result attached to a consolidated finding (Milestone
/// 014.4). Present ONLY for the narrow ObservationType-disagreement subset, and only
/// when semantic reconciliation is enabled. Additive — a consumer may ignore it.
/// </summary>
public sealed record SemanticReviewContract
{
    [JsonPropertyName("decision")] public string? Decision { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

/// <summary>
/// Deterministic Council assessment attached to a consolidated finding (Milestone 014.2).
/// Additive with respect to the integration contract — a consumer may ignore it.
/// </summary>
public sealed record CouncilAssessmentContract
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("supportingProviders")] public List<string> SupportingProviders { get; init; } = [];
    [JsonPropertyName("agreementCount")] public int AgreementCount { get; init; }
    [JsonPropertyName("differences")] public List<string> Differences { get; init; } = [];
    [JsonPropertyName("contradictionReasons")] public List<string> ContradictionReasons { get; init; } = [];
}

/// <summary>
/// Deterministic Council assessment counts across consolidated findings (Milestone
/// 014.3). Additive with respect to the integration contract — a consumer may ignore it.
/// </summary>
public sealed record CouncilAssessmentSummaryContract
{
    [JsonPropertyName("singleSourceCount")] public int SingleSourceCount { get; init; }
    [JsonPropertyName("strongAgreementCount")] public int StrongAgreementCount { get; init; }
    [JsonPropertyName("agreementWithDifferencesCount")] public int AgreementWithDifferencesCount { get; init; }
    [JsonPropertyName("potentialConflictCount")] public int PotentialConflictCount { get; init; }
}

/// <summary>
/// Deterministic reproducibility identity of the analyzed repository state
/// (Milestone 015.3C). Additive with respect to the integration contract — a
/// consumer may ignore it, but it answers "what exact source state does this
/// review describe?". No absolute local paths are exposed.
/// </summary>
public sealed record RepositorySnapshotContract
{
    [JsonPropertyName("versionControl")] public string? VersionControl { get; init; }
    [JsonPropertyName("commitSha")] public string? CommitSha { get; init; }
    [JsonPropertyName("branch")] public string? Branch { get; init; }
    [JsonPropertyName("isDirty")] public bool? IsDirty { get; init; }
    [JsonPropertyName("hasUntrackedFiles")] public bool? HasUntrackedFiles { get; init; }
    [JsonPropertyName("snapshotFingerprint")] public string? SnapshotFingerprint { get; init; }
    [JsonPropertyName("repositoryChangedDuringRun")] public bool RepositoryChangedDuringRun { get; init; }
}

public sealed record ReconciliationContract
{
    [JsonPropertyName("rawFindingCount")] public int RawFindingCount { get; init; }
    [JsonPropertyName("consolidatedFindingCount")] public int ConsolidatedFindingCount { get; init; }
    [JsonPropertyName("multiProviderFindingCount")] public int MultiProviderFindingCount { get; init; }
    [JsonPropertyName("contradictionCount")] public int ContradictionCount { get; init; }
    [JsonPropertyName("findingsByProvider")] public Dictionary<string, int> FindingsByProvider { get; init; } = [];
    // Deterministic dedup diagnostics (M15.3D) — additive; a consumer may ignore them.
    [JsonPropertyName("preDedupFindingCount")] public int PreDedupFindingCount { get; init; }
    [JsonPropertyName("postDedupFindingCount")] public int PostDedupFindingCount { get; init; }
    [JsonPropertyName("deduplicatedFindingCount")] public int DeduplicatedFindingCount { get; init; }
}

public sealed record AcquisitionCoverageContract
{
    [JsonPropertyName("disciplinesRequested")] public List<string> DisciplinesRequested { get; init; } = [];
    [JsonPropertyName("disciplinesCovered")] public List<string> DisciplinesCovered { get; init; } = [];
}

/// <summary>
/// Deterministic per-discipline evidence coverage (Milestone 015.3A). Additive with
/// respect to the integration contract — a consumer may ignore it, but it is the
/// authoritative way to interpret "zero findings" (absence of evidence is not
/// assurance).
/// </summary>
public sealed record DisciplineCoverageContract
{
    [JsonPropertyName("entries")] public List<DisciplineCoverageEntryContract> Entries { get; init; } = [];
}

public sealed record DisciplineCoverageEntryContract
{
    [JsonPropertyName("discipline")] public string? Discipline { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("successfulProviders")] public int SuccessfulProviders { get; init; }
    [JsonPropertyName("attemptedProviders")] public int AttemptedProviders { get; init; }
}

public sealed record StaticSourceContract
{
    [JsonPropertyName("tool")] public string? Tool { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("importedResults")] public int ImportedResults { get; init; }
    [JsonPropertyName("generatedObservations")] public int GeneratedObservations { get; init; }
}

public sealed record MetricsContract
{
    [JsonPropertyName("totalFindings")] public int TotalFindings { get; init; }
    [JsonPropertyName("totalObservations")] public int TotalObservations { get; init; }
}
