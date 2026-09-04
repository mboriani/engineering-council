namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A single actionable engineering improvement opportunity produced by an analyzer.
/// This is the primary contract consumed by the future Engineering Dashboard, so
/// the shape is deliberately explicit and stable.
/// </summary>
public sealed record Finding
{
    /// <summary>Stable identifier for this finding within its run.</summary>
    public required string Id { get; init; }

    /// <summary>Short human-readable headline.</summary>
    public required string Title { get; init; }

    /// <summary>Discipline of the finding. Defaults to <see cref="FindingCategory.Unknown"/> — never implicitly CodeQuality.</summary>
    public FindingCategory Category { get; init; } = FindingCategory.Unknown;

    public FindingSeverity Severity { get; init; } = FindingSeverity.Medium;

    public FindingConfidence Confidence { get; init; } = FindingConfidence.Medium;

    /// <summary>One or two sentence summary suitable for a list view.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Full explanation of the issue.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Concrete evidence (quotes, symbol names, patterns) supporting the finding.</summary>
    public string Evidence { get; init; } = string.Empty;

    /// <summary>Files/locations that back the finding.</summary>
    public IReadOnlyList<FileReference> FileReferences { get; init; } = [];

    /// <summary>Symbols (types/members) the finding refers to, when known.</summary>
    public IReadOnlyList<string> SymbolReferences { get; init; } = [];

    /// <summary>1-based line numbers the finding refers to, when known.</summary>
    public IReadOnlyList<int> LineReferences { get; init; } = [];

    /// <summary>Why this matters for the team / business.</summary>
    public string WhyItMatters { get; init; } = string.Empty;

    /// <summary>Actionable recommendation.</summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>Suggested title for a backlog ticket.</summary>
    public string SuggestedTicketTitle { get; init; } = string.Empty;

    /// <summary>Suggested body for a backlog ticket.</summary>
    public string SuggestedTicketDescription { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Which agent produced the finding. Kept for backward compatibility; for a
    /// consolidated (merged) finding this holds the primary analyzer. Prefer
    /// <see cref="SourceAgents"/> going forward.
    /// </summary>
    public string SourceAgent { get; init; } = "analyzer-agent";

    // ── Consolidation layer (Milestone 003) ──────────────────────────────────

    /// <summary>Lifecycle state after merging. Raw findings are <see cref="FindingStatus.New"/>.</summary>
    public FindingStatus Status { get; init; } = FindingStatus.New;

    /// <summary>When this finding is a <see cref="FindingStatus.Duplicate"/>, the id it was folded into.</summary>
    public string? DuplicateOf { get; init; }

    /// <summary>Ids of the raw findings this consolidated finding was merged from (empty if standalone).</summary>
    public IReadOnlyList<string> MergedFromFindingIds { get; init; } = [];

    /// <summary>All analyzers that contributed to this finding (future-facing multi-agent attribution).</summary>
    public IReadOnlyList<string> SourceAgents { get; init; } = [];

    /// <summary>
    /// The evidence provider whose evidence this finding was interpreted from
    /// (e.g. "Claude", "Mock"). Preserved so provider attribution survives even
    /// though no cross-provider merge happens yet.
    /// </summary>
    public string EvidenceProvider { get; init; } = string.Empty;

    // ── Observation-based provenance (Milestone 007) ─────────────────────────
    // Finding → EngineeringObservation → Evidence → provider execution.

    /// <summary>Ids of the observations this finding was reasoned from.</summary>
    public IReadOnlyList<string> ObservationIds { get; init; } = [];

    /// <summary>Provider/tool rule ids that backed the supporting observations.</summary>
    public IReadOnlyList<string> SourceRules { get; init; } = [];

    /// <summary>Distinct evidence providers that contributed supporting observations.</summary>
    public IReadOnlyList<string> SupportingProviders { get; init; } = [];

    /// <summary>How many observations support this finding.</summary>
    public int SupportingObservationCount { get; init; }

    /// <summary>Why the final severity was chosen (e.g. escalation across merged findings).</summary>
    public string SeverityRationale { get; init; } = string.Empty;

    /// <summary>Why the final confidence was chosen (e.g. corroboration across analyzers).</summary>
    public string ConfidenceRationale { get; init; } = string.Empty;

    // ── Multi-source reconciliation (Milestone 010) ──────────────────────────
    // Additive fields describing how a consolidated finding was reconciled across
    // multiple evidence sources. For a standalone finding these carry single-source
    // defaults. `Category` is the discipline; `Recommendation` the recommendation.

    /// <summary>Raw analyzer finding ids that were reconciled into this finding (⊇ <see cref="MergedFromFindingIds"/>).</summary>
    public IReadOnlyList<string> SupportingFindingIds { get; init; } = [];

    /// <summary>Number of INDEPENDENT providers that agree on this finding (not the number of findings).</summary>
    public int AgreementCount { get; init; } = 1;

    /// <summary>Range of severities across the reconciled sources, e.g. "Medium–High" (single value if all agree).</summary>
    public string SeverityRange { get; init; } = string.Empty;

    /// <summary>Range of confidences across the reconciled sources, e.g. "Medium–High".</summary>
    public string ConfidenceRange { get; init; } = string.Empty;

    /// <summary>Human-readable reason the sources were reconciled together (or kept standalone).</summary>
    public string ReconciliationReason { get; init; } = string.Empty;

    /// <summary>The deterministic reconciliation strategy that grouped the sources (e.g. "exact-rule-location").</summary>
    public string ReconciliationStrategy { get; init; } = string.Empty;

    /// <summary>True when this finding consolidates two or more raw findings.</summary>
    public bool IsConsolidated { get; init; }

    /// <summary>True when reconciled sources materially disagree (recorded, not silently merged away).</summary>
    public bool HasContradiction { get; init; }

    /// <summary>Explicit reasons the reconciled sources disagree (severity spread, disjoint files, …).</summary>
    public IReadOnlyList<string> ContradictionReasons { get; init; } = [];

    /// <summary>Free-form additive metadata (e.g. reconciliation match score). Provider-neutral.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    // ── Deterministic Council assessment (Milestone 014.2) ─────────────────────
    // Provider-neutral classification of how strongly independent providers agree
    // on this finding. Computed only from existing authoritative data (reconciler
    // fields + M12.3/M12.4 calibration diagnostics) — never an LLM. Absent on raw
    // findings; stamped on consolidated findings by the package builder.

    /// <summary>The deterministic Council assessment, when this finding has been assessed.</summary>
    public ReconciliationAssessment? CouncilAssessment { get; init; }

    // ── Targeted semantic reconciliation (Milestone 014.4) ─────────────────────
    // OPTIONAL, disabled-by-default enrichment for the narrow subset of findings
    // whose CouncilAssessment flags an ObservationType disagreement. Never changes
    // severity/confidence/recommendation/grouping — advisory provenance only.

    /// <summary>
    /// The targeted semantic review result, populated ONLY when this finding was a
    /// semantic-review candidate (an <c>AgreementWithDifferences</c> assessment whose
    /// <c>Differences</c> include <c>ObservationType</c>) and the feature was enabled.
    /// </summary>
    public SemanticReconciliationResult? SemanticReview { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
