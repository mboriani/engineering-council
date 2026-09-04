using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Resolves the correct <see cref="IEvidenceInterpreter"/> for a given
/// <see cref="Evidence"/> object by discovery (each interpreter's
/// <see cref="IEvidenceInterpreter.CanInterpret"/>), not a provider-name switch.
/// Returns <see langword="null"/> when no interpreter supports the format, so the
/// caller can report unsupported evidence rather than invent observations.
/// </summary>
public interface IEvidenceInterpreterResolver
{
    IEvidenceInterpreter? Resolve(Evidence evidence);
}
