using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// A provider-neutral evidence prompt: the system instruction defining the source's
/// role, and the user content carrying the discipline objective, the strict output
/// schema, and ONLY the context the selector chose. Provider adapters translate this
/// into their own message shapes — they never rebuild the engineering prompt.
/// </summary>
public sealed record EvidencePrompt
{
    public required string SystemInstructions { get; init; }
    public required string UserContent { get; init; }
    public required FindingCategory? Discipline { get; init; }

    /// <summary>Schema version the provider is asked to emit.</summary>
    public string ResponseSchemaVersion { get; init; } = "1.0";

    /// <summary>Characters of repository context included (telemetry; no content).</summary>
    public int ContextCharacterCount { get; init; }

    public int ContextFileCount { get; init; }
}

/// <summary>
/// Builds the shared, provider-neutral <see cref="EvidencePrompt"/> for one
/// acquisition step. One implementation serves every LLM provider so Claude and
/// OpenAI are asked for exactly the same structured evidence.
/// </summary>
public interface IEvidencePromptBuilder
{
    EvidencePrompt Build(EvidenceRequest request);
}
