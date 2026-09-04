namespace EngineeringCouncil.Core.Domain;

/// <summary>Supporting appendix material for the review package.</summary>
public sealed record ReviewAppendix
{
    /// <summary>Original per-analyzer findings, before consolidation.</summary>
    public IReadOnlyList<Finding> RawFindings { get; init; } = [];

    /// <summary>Human-readable notes about how findings were merged.</summary>
    public IReadOnlyList<string> MergeNotes { get; init; } = [];

    /// <summary>Deterministic reconciliation groups (traceability; Milestone 010).</summary>
    public IReadOnlyList<ReconciliationGroup> ReconciliationGroups { get; init; } = [];
}

/// <summary>
/// The <b>Engineering Review Package</b> — the first-class deliverable of the
/// platform and the object an Engineering Review Board consumes. It is the
/// complete engineering assessment of a repository at a point in time; Markdown,
/// JSON, and (future) HTML/PDF are only projections of it.
/// </summary>
public sealed record EngineeringReviewPackage
{
    /// <summary>Package schema version (legacy field, retained for backward compatibility).</summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Explicit integration schema version for the external Engineering Review
    /// application. Additive fields bump the minor; breaking changes bump the major.
    /// 1.1 adds multi-source reconciliation fields (Milestone 010).
    /// </summary>
    public string SchemaVersion { get; init; } = "1.1";

    // Repository identity ────────────────────────────────────────────────────
    public required string Repository { get; init; }
    public string Branch { get; init; } = "(unknown)";
    public string Commit { get; init; } = "(unknown)";

    /// <summary>
    /// Deterministic reproducibility identity of the analyzed repository state
    /// (Milestone 015.3C): CommitSha, Branch, IsDirty, HasUntrackedFiles,
    /// SnapshotFingerprint and RepositoryChangedDuringRun. Additive to the external
    /// contract — consumers can answer "what exact source state does this review
    /// describe?". No absolute local paths are exposed. Null for pre-M15.3C runs.
    /// </summary>
    public RepositorySnapshotIdentity? RepositorySnapshot { get; init; }

    // Provenance ─────────────────────────────────────────────────────────────
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public required string AnalysisRunId { get; init; }
    public TimeSpan AnalysisDuration { get; init; }

    // Executive view ─────────────────────────────────────────────────────────
    public string ExecutiveSummary { get; init; } = string.Empty;
    public EngineeringHealth OverallEngineeringHealth { get; init; }
    public EngineeringRisk OverallRisk { get; init; }
    public IReadOnlyList<string> KeyStrengths { get; init; } = [];
    public IReadOnlyList<string> KeyRisks { get; init; } = [];
    public IReadOnlyList<string> RecommendedNextActions { get; init; } = [];

    // Assessment body ────────────────────────────────────────────────────────
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public CouncilSummary? CouncilSummary { get; init; }

    /// <summary>The evidence acquisition plan that was executed (topology of the run).</summary>
    public EvidenceAcquisitionPlan? AcquisitionPlan { get; init; }

    /// <summary>Generic, provider-agnostic summary of what acquisition covered.</summary>
    public AcquisitionCoverage AcquisitionCoverage { get; init; } = new();

    /// <summary>
    /// Deterministic per-discipline evidence coverage (Milestone 015.3A): for each
    /// requested discipline, whether evidence was acquired and findings exist
    /// (<see cref="DisciplineCoverageStatus.CoveredWithFindings"/> /
    /// <see cref="DisciplineCoverageStatus.CoveredNoFindings"/> /
    /// <see cref="DisciplineCoverageStatus.NoEvidence"/>), plus successful/attempted
    /// provider counts. Additive, provider-neutral, computed ONCE by the package
    /// builder from authoritative run facts — never recomputed by exporters. Consumers
    /// need it to correctly interpret "zero findings": absence of evidence is not
    /// assurance. Null when no disciplines were requested.
    /// </summary>
    public DisciplineCoverage? DisciplineCoverage { get; init; }

    /// <summary>Deterministic static-analysis sources imported for this run (e.g. SARIF tools).</summary>
    public IReadOnlyList<StaticAnalysisSource> StaticAnalysisSources { get; init; } = [];

    public ProviderExecutionReport? ProviderExecution { get; init; }

    /// <summary>Provider-neutral summary of deterministic multi-source reconciliation (Milestone 010).</summary>
    public ReconciliationSummary? Reconciliation { get; init; }

    public EvidenceSummary EvidenceSummary { get; init; } = new();
    public EngineeringMetrics Metrics { get; init; } = new();
    public ReviewAppendix Appendix { get; init; } = new();

    /// <summary>
    /// Deterministic counts of the Council assessment types across consolidated
    /// findings (Milestone 014.3) — a pure aggregation of each finding's own
    /// <c>councilAssessment</c>, never a new calculation. Additive to the contract.
    /// </summary>
    public CouncilAssessmentSummary? CouncilAssessmentSummary { get; init; }
}
