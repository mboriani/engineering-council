using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Builds the single focused, read-only analysis instruction handed to the Claude
/// Code agentic runtime (Milestone 013.5). It reuses the existing LLM discipline
/// instructions and the existing structured <c>observations</c> output contract —
/// there is deliberately NO Claude-Code-specific finding/observation format. The agent
/// is told the repository is available in the current working directory, that access
/// is READ ONLY, and that it must report observations/evidence only (never the final
/// Engineering Review). The prompt is deliberately NOT tuned to any specific Claude
/// model: the provider is Claude Code; model selection is configuration.
///
/// IMPORTANT: the read-only boundary is enforced BOTH by this instruction AND by the
/// Claude Code runtime's own supported tool restriction — the provider launches with
/// <c>--tools "Read,Glob,Grep"</c>, which removes write-capable built-in tools
/// (Edit/Write/Bash/MultiEdit) from the available toolset entirely. This is still
/// not an OS-level sandbox (sandboxing is unsupported on Windows); OS
/// sandboxing/containers remain deferred.
/// </summary>
public static class ClaudeCodePromptBuilder
{
    private const string AgentRole =
        "You are an engineering evidence acquisition agent for an automated review platform.\n" +
        "Your ONLY job is to identify candidate engineering OBSERVATIONS for the requested discipline " +
        "by exploring the repository yourself. You are NOT the reviewer.\n" +
        "You must NOT produce a final engineering review, a report, an Engineering Review Package, " +
        "tickets, pull requests, code changes, or Markdown documents. Report observations/evidence only.";

    private const string RepositoryNote =
        "# Repository\n" +
        "The analyzed repository is available in the current working directory. Explore it yourself: " +
        "list files, search code, and inspect the project structure to ground your evidence. " +
        "The repository is not embedded in this prompt.";

    private const string ReadOnlyBoundary =
        "# Access boundary\n" +
        "Repository access is READ ONLY. You may list files, read files, search code, and inspect the project structure.\n" +
        "You must NOT modify source files, create files, delete files, run formatting, commit changes, or generate patches.";

    /// <summary>
    /// Builds the complete analysis instruction for one acquisition step. The requested
    /// discipline, the discipline instructions, and the existing structured evidence
    /// schema are all carried; the agent's FINAL message must be a single
    /// <c>observations</c> envelope compatible with <c>schemaVersion "1.0"</c> — that
    /// final text is what the CLI reports in its <c>result</c> field.
    /// </summary>
    public static string Build(EvidenceRequest request)
    {
        var instructions = string.IsNullOrWhiteSpace(request.Instructions)
            ? DisciplinePrompts.BuildInstructions(request.Scope, request.Discipline)
            : request.Instructions;

        return $"""
            {AgentRole}

            {RepositoryNote}

            {ReadOnlyBoundary}

            # Requested discipline
            {request.Discipline?.ToString() ?? "Repository"}

            {instructions}
            """;
    }
}
