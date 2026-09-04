using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Converts one provider-specific raw <see cref="Evidence"/> object into zero,
/// one, or many normalized <see cref="EngineeringObservation"/>s. It understands
/// a specific evidence format (structured LLM output, SARIF, a SonarQube report,
/// git log, coverage, …).
///
/// An interpreter MUST NOT: create <see cref="Finding"/>s, write files, generate
/// Markdown, or know about the Engineering Review Package.
/// </summary>
public interface IEvidenceInterpreter
{
    /// <summary>Identifier for this interpreter (for telemetry/diagnostics).</summary>
    string ProviderName { get; }

    /// <summary>True when this interpreter understands the given evidence's format.</summary>
    bool CanInterpret(Evidence evidence);

    Task<IReadOnlyList<EngineeringObservation>> InterpretAsync(
        Evidence evidence,
        AnalyzerContext context,
        CancellationToken cancellationToken = default);
}
