namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// How an evidence source is executed against a repository.
/// </summary>
public enum EvidenceAcquisitionScope
{
    /// <summary>Executed once over the whole repository (e.g. SonarQube, SARIF, Roslyn, Git, coverage).</summary>
    Repository = 0,

    /// <summary>Executed once per selected discipline with a focused request (e.g. an LLM reviewer).</summary>
    Discipline = 1
}
