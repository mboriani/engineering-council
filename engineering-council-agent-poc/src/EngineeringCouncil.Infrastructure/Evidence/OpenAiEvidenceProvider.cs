using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Real OpenAI evidence provider (Milestone 011). Discipline-scoped, sharing the same
/// prompt, schema, validation and telemetry as every other LLM source. A code-focused
/// model is simply the configured <c>Evidence:OpenAI:Model</c>. The "Codex" name is
/// NOT an alias for this provider (Milestone 013.4): "Codex" now selects the real
/// agentic Codex CLI provider; the direct OpenAI API provider is selected as "OpenAI".
/// </summary>
public sealed class OpenAiEvidenceProvider : LlmEvidenceProvider
{
    public OpenAiEvidenceProvider(
        IOpenAiClient client,
        OpenAiProviderOptions options,
        IEvidencePromptBuilder promptBuilder,
        ILogger<OpenAiEvidenceProvider>? logger = null)
        : base(client, options, promptBuilder, logger)
    {
    }
}
