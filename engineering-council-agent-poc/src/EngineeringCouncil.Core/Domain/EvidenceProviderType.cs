namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The kind of source an <c>IEvidenceProvider</c> represents. The council reasons
/// over engineering <see cref="Evidence"/> regardless of where it came from, so
/// language models and static tools sit behind the same contract.
/// </summary>
public enum EvidenceProviderType
{
    /// <summary>A language model (Claude, Codex, GPT, Ollama, …).</summary>
    LLM = 0,

    /// <summary>A static analyzer (Roslyn, SonarQube, Semgrep, NDepend, …).</summary>
    StaticAnalyzer = 1,

    /// <summary>Source-control history/metadata (git blame, churn, …).</summary>
    SourceControl = 2,

    /// <summary>Documentation sources.</summary>
    Documentation = 3,

    /// <summary>Any other external tool.</summary>
    ExternalTool = 4,

    /// <summary>A bespoke, in-house provider.</summary>
    Custom = 5,

    /// <summary>
    /// An external agent (Codex, Claude Code, opencode, …) that explores the
    /// repository itself. The council hands it the repository root, discipline,
    /// instructions, correlation metadata and output contract — but NOT a bundled
    /// context or a fabricated context fingerprint (the council does not control
    /// what the agent sees).
    /// </summary>
    Agentic = 6
}
