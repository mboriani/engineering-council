using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// A first-class request handed to an <c>IEvidenceProvider</c> for one acquisition
/// step. It carries the scope (repository-wide vs discipline-specific), the target
/// discipline (null when repository-wide), the discipline instructions, and the
/// deterministic <see cref="AnalysisContextSelection"/>. The raw
/// <see cref="RepositorySnapshot"/> is included so repository-wide static sources
/// can analyze the whole tree.
///
/// A provider RETURNS EVIDENCE ONLY — never findings.
/// </summary>
public sealed record EvidenceRequest
{
    public required string RunId { get; init; }
    public required RepositorySnapshot RepositorySnapshot { get; init; }
    public required EvidenceAcquisitionScope Scope { get; init; }

    /// <summary>Target discipline for a discipline-scoped request; null for repository-wide.</summary>
    public FindingCategory? Discipline { get; init; }

    /// <summary>Discipline (or repository-wide) instructions asking for structured observations.</summary>
    public required string Instructions { get; init; }

    /// <summary>The deterministic repository-context selection for this request.</summary>
    public required AnalysisContextSelection ContextSelection { get; init; }

    /// <summary>Providers this request targets (usually the single step provider).</summary>
    public required IReadOnlyList<string> ProviderNames { get; init; }

    /// <summary>Correlates the request to its acquisition step, evidence, observations and telemetry.</summary>
    public required string CorrelationId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Optional additional context for agentic providers. When present, this context is
    /// included in the agent prompt as a clearly delimited section. It serves as a
    /// navigation/supporting map — agents must verify findings against repository source
    /// and may explore outside this context.
    /// </summary>
    public string? AdditionalContext { get; init; }
}
