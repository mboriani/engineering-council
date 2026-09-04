using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// One supporting provider's observation for an ambiguous consolidated finding —
/// the MINIMAL context a semantic reviewer needs about a single observation.
/// </summary>
public sealed record SemanticReconciliationObservationInput
{
    public required string Provider { get; init; }
    public required string ObservationType { get; init; }
    public IReadOnlyList<string> FileReferences { get; init; } = [];

    /// <summary>A short, already-captured evidence excerpt (never re-fetched from the repository).</summary>
    public string EvidenceExcerpt { get; init; } = string.Empty;
}

/// <summary>
/// The MINIMAL, finding-scoped input for a targeted semantic review (Milestone
/// 014.4): the ambiguous consolidated finding's own identity plus the attributed
/// observations whose type disagreement triggered the review. Deliberately
/// excludes the rest of the repository, unrelated findings, the full
/// <c>EngineeringReviewPackage</c>, other Council results, provider ranking/history,
/// and any previous model reasoning — this is targeted, not general-purpose, review.
/// </summary>
public sealed record SemanticReconciliationRequest
{
    public required string FindingId { get; init; }
    public required string Title { get; init; }
    public required FindingCategory Category { get; init; }
    public required IReadOnlyList<string> SupportingProviders { get; init; }
    public required IReadOnlyList<SemanticReconciliationObservationInput> Observations { get; init; }
}

/// <summary>
/// A targeted semantic reviewer (Milestone 014.4). It answers ONLY: "do these
/// already-reconciled provider observations describe the same underlying
/// engineering issue?" — for a SINGLE ambiguous consolidated finding at a time.
///
/// It is NOT a judge of the whole Council: it never sees the repository, other
/// findings, or the full package, and it never decides severity, confidence,
/// recommendation, or which provider is "right". Implementations are replaceable —
/// the domain and the caller never hardcode a specific provider (Claude, OpenAI,
/// DeepSeek, Codex, OpenCode, …).
/// </summary>
public interface ISemanticReconciliationReviewer
{
    Task<SemanticReconciliationResult> ReviewAsync(
        SemanticReconciliationRequest request,
        CancellationToken cancellationToken = default);
}
