using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Real Anthropic Claude evidence provider (Milestone 011). Discipline-scoped: it
/// executes once per selected discipline with the shared, provider-neutral prompt and
/// returns the same structured evidence schema as every other LLM source. All Claude
/// wire-format concerns live in <see cref="IClaudeClient"/>; all shared behaviour
/// (retry, timeout, validation, one repair, telemetry) lives in
/// <see cref="LlmEvidenceProvider"/> — so nothing in the platform branches on "Claude".
/// </summary>
public sealed class ClaudeEvidenceProvider : LlmEvidenceProvider
{
    public ClaudeEvidenceProvider(
        IClaudeClient client,
        ClaudeProviderOptions options,
        IEvidencePromptBuilder promptBuilder,
        ILogger<ClaudeEvidenceProvider>? logger = null)
        : base(client, options, promptBuilder, logger)
    {
    }
}
