using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Interpretation;

// Contract-level skeletons for future evidence formats. They are intentionally
// NOT registered in DI, so they never participate in discovery — and they do NOT
// throw NotImplementedException. Each declares its format via CanInterpret and,
// until implemented, matches nothing and produces nothing. When a real provider
// and interpreter are built, register the interpreter and flip CanInterpret.
//
// Planned: SonarQube, Roslyn, Semgrep, NDepend, Git history, coverage.
// (SARIF is implemented — see SarifEvidenceInterpreter.)

/// <summary>Base for planned-but-unimplemented interpreters (never throws, never matches).</summary>
public abstract class PlannedEvidenceInterpreter : IEvidenceInterpreter
{
    public abstract string ProviderName { get; }

    /// <summary>Metadata: not implemented/registered yet.</summary>
    public bool IsAvailable => false;

    public bool CanInterpret(Core.Domain.Evidence evidence) => false;

    public Task<IReadOnlyList<EngineeringObservation>> InterpretAsync(
        Core.Domain.Evidence evidence, AnalyzerContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<EngineeringObservation>>([]);
}

public sealed class SonarEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "sonar"; }
public sealed class RoslynEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "roslyn"; }
public sealed class SemgrepEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "semgrep"; }
public sealed class NDependEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "ndepend"; }
public sealed class GitEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "git"; }
public sealed class CoverageEvidenceInterpreter : PlannedEvidenceInterpreter { public override string ProviderName => "coverage"; }
