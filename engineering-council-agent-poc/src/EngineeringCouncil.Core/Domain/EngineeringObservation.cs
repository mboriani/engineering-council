namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A normalized, source-neutral engineering observation — the common language
/// between heterogeneous evidence and the analyzers. An observation is produced
/// by an <c>IEvidenceInterpreter</c> from one provider-specific
/// <see cref="Evidence"/> object. It is NOT a finding: it is a single normalized
/// signal that analyzers reason over to reach actionable conclusions.
/// </summary>
public sealed record EngineeringObservation
{
    /// <summary>Stable id within a run (e.g. "OBS-001").</summary>
    public required string Id { get; init; }

    /// <summary>Open string type (see <see cref="ObservationTypes"/>), not a closed enum.</summary>
    public string ObservationType { get; init; } = ObservationTypes.GeneralObservation;

    /// <summary>
    /// Engineering discipline this observation belongs to. Defaults to
    /// <see cref="FindingCategory.Unknown"/> — a discipline-less observation is never
    /// implicitly claimed by CodeQuality.
    /// </summary>
    public FindingCategory Discipline { get; init; } = FindingCategory.Unknown;

    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>Reuses the shared engineering severity model.</summary>
    public FindingSeverity Severity { get; init; } = FindingSeverity.Medium;

    /// <summary>Confidence in this NORMALIZED observation (not the final finding).</summary>
    public FindingConfidence Confidence { get; init; } = FindingConfidence.Medium;

    // Provenance back to the evidence + provider ──────────────────────────────
    public string SourceProvider { get; init; } = string.Empty;
    public EvidenceProviderType SourceProviderType { get; init; } = EvidenceProviderType.LLM;
    public string SourceEvidenceId { get; init; } = string.Empty;

    /// <summary>Provider/tool rule id, when the source is rule-based (e.g. a Roslyn/Semgrep rule).</summary>
    public string? RuleId { get; init; }

    public IReadOnlyList<FileReference> FileReferences { get; init; } = [];
    public IReadOnlyList<string> SymbolReferences { get; init; } = [];
    public IReadOnlyList<int> LineReferences { get; init; } = [];

    /// <summary>1-based column references, when the source (e.g. SARIF region) provides them.</summary>
    public IReadOnlyList<int> ColumnReferences { get; init; } = [];

    /// <summary>A short excerpt of the underlying evidence supporting the observation.</summary>
    public string EvidenceExcerpt { get; init; } = string.Empty;

    /// <summary>A non-authoritative hint; analyzers decide the final recommendation.</summary>
    public string RecommendationHint { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    // ── Acquisition provenance (Milestone 008) ────────────────────────────────
    public EvidenceAcquisitionScope AcquisitionScope { get; init; } = EvidenceAcquisitionScope.Repository;

    /// <summary>Discipline the underlying evidence request targeted; null for repository-wide.</summary>
    public FindingCategory? RequestedDiscipline { get; init; }

    /// <summary>Correlates back to the acquisition step / evidence request / context selection.</summary>
    public string AcquisitionCorrelationId { get; init; } = string.Empty;

    /// <summary>The acquisition step whose evidence produced this observation.</summary>
    public string AcquisitionStepId { get; init; } = string.Empty;

    public int ContextFileCount { get; init; }
    public string ContextSelectionStrategy { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
