using EngineeringCouncil.Core.Abstractions;

namespace EngineeringCouncil.Core.Analysis;

/// <summary>
/// The single, provider-neutral prompt builder used by EVERY LLM evidence provider
/// (Milestone 011). Claude and OpenAI receive exactly the same engineering prompt and
/// are asked for exactly the same structured schema; provider adapters only translate
/// it into their own message objects.
///
/// The prompt carries ONLY the context the <c>IAnalysisContextSelector</c> chose —
/// providers never rescan the repository and never see files outside the selection.
/// </summary>
public sealed class DisciplineEvidencePromptBuilder : IEvidencePromptBuilder
{
    private readonly RepositoryContextBuilder _contextBuilder;

    public DisciplineEvidencePromptBuilder(RepositoryContextBuilder? contextBuilder = null)
        => _contextBuilder = contextBuilder ?? new RepositoryContextBuilder();

    public EvidencePrompt Build(EvidenceRequest request)
    {
        // Instructions = shared constraints + discipline objective + strict output contract.
        var instructions = string.IsNullOrWhiteSpace(request.Instructions)
            ? DisciplinePrompts.BuildInstructions(request.Scope, request.Discipline)
            : request.Instructions;

        var context = _contextBuilder.Build(request.RepositorySnapshot, request.ContextSelection);
        var discipline = request.Discipline?.ToString() ?? "Repository";

        var userContent = $"""
            {instructions}

            # Requested discipline
            {discipline}

            {context}
            """;

        return new EvidencePrompt
        {
            SystemInstructions = DisciplinePrompts.SystemInstructions,
            UserContent = userContent,
            Discipline = request.Discipline,
            ResponseSchemaVersion = "1.0",
            ContextCharacterCount = userContent.Length,
            ContextFileCount = request.ContextSelection.SelectedFileCount
        };
    }
}
