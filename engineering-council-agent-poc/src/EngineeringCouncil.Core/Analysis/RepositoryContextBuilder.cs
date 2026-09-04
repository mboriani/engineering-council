using System.Text;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Analysis;

/// <summary>
/// Builds the textual repository context sent to an LLM-style provider from the
/// files chosen by an <c>IAnalysisContextSelector</c> — only the selected files
/// (so a discipline request stays focused). Each file is rendered up to the shared
/// per-file limit from <see cref="ContextContentPolicy"/> — the SAME rule the
/// selector uses when budgeting — so the rendered context matches the selection's
/// effective-size accounting. The observation output contract lives in
/// <see cref="DisciplinePrompts"/>, not here.
/// </summary>
public sealed class RepositoryContextBuilder
{
    private readonly ContextContentPolicy _policy;

    public RepositoryContextBuilder(ContextContentPolicy? policy = null)
        => _policy = policy ?? new ContextContentPolicy();

    public string Build(RepositorySnapshot snapshot, AnalysisContextSelection selection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Repository context");
        sb.AppendLine();
        sb.AppendLine($"- Solution: {snapshot.SolutionName}");
        sb.AppendLine($"- Selection strategy: {selection.Strategy}");
        sb.AppendLine($"- Files in this context: {selection.Files.Count}");
        sb.AppendLine();
        sb.AppendLine("Only the following real files exist in this context. Do not reference any others.");
        sb.AppendLine();

        foreach (var file in selection.Files)
        {
            var content = string.IsNullOrEmpty(file.Content)
                ? "/* (no content loaded) */"
                : Truncate(file.Content!, _policy.MaxCharactersPerFile);
            sb.Append($"### {file.RelativePath} ({file.LineCount} lines)\n```{Lang(file.Extension)}\n{content}\n```\n\n");
        }

        return sb.ToString();
    }

    private static string Truncate(string content, int max)
        => content.Length <= max ? content : content[..max] + "\n/* …truncated… */";

    private static string Lang(string ext) => ext switch
    {
        ".cs" => "csharp",
        ".csproj" or ".props" or ".targets" => "xml",
        ".json" => "json",
        ".yml" or ".yaml" => "yaml",
        _ => ""
    };
}
