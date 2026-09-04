using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Interpretation;

/// <summary>
/// Discovers registered interpreters from DI and resolves the first one that can
/// interpret a given evidence object. No provider-name switch — resolution is by
/// each interpreter's <see cref="IEvidenceInterpreter.CanInterpret"/>. Returns
/// null when nothing matches so the caller can record unsupported evidence.
/// </summary>
public sealed class EvidenceInterpreterResolver : IEvidenceInterpreterResolver
{
    private readonly IReadOnlyList<IEvidenceInterpreter> _interpreters;

    public EvidenceInterpreterResolver(IEnumerable<IEvidenceInterpreter> interpreters)
        => _interpreters = interpreters.ToList();

    public IEvidenceInterpreter? Resolve(Core.Domain.Evidence evidence)
        => _interpreters.FirstOrDefault(i => i.CanInterpret(evidence));
}
