using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// A source of engineering evidence. A provider RETURNS EVIDENCE ONLY — it must
/// never create <see cref="Finding"/>s. Its identity and acquisition behavior are
/// declared in <see cref="Metadata"/>; an LLM source uses the request's discipline
/// instructions + selected context, while a repository-wide static source may
/// ignore the discipline fields and analyze the whole snapshot.
/// </summary>
public interface IEvidenceProvider
{
    /// <summary>Identity + acquisition behavior (name, type, scope, disciplines).</summary>
    EvidenceProviderMetadata Metadata { get; }

    /// <summary>
    /// False when the provider cannot currently be used (e.g. missing API key or
    /// the tool is not installed). Used for graceful fallback.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A secret-safe explanation of why the provider is unavailable (e.g. which
    /// environment variable is missing), or null when it can run. Never contains the
    /// secret itself. Defaulted so existing providers need no change.
    /// </summary>
    string? UnavailableReason => null;

    /// <summary>
    /// Collects evidence for one acquisition step. Most sources return a single
    /// item, but a source may return several — e.g. the SARIF source produces one
    /// <see cref="Evidence"/> per imported tool run. Returning an empty list is
    /// valid (nothing to import). A provider RETURNS EVIDENCE ONLY — never findings.
    /// </summary>
    Task<IReadOnlyList<Evidence>> CollectAsync(
        EvidenceRequest request,
        CancellationToken cancellationToken = default);
}
