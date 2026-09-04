using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Analysis;

/// <summary>
/// The single, authoritative rule for how much repository content a context
/// selection may consume and how it is measured. Both the context selector
/// (budgeting) and the context renderer (truncation) use this policy, so
/// selection accounting and the rendered context always agree.
///
/// A file is never rendered beyond <see cref="MaxCharactersPerFile"/>, so a file
/// whose raw content exceeds that limit contributes only its effective
/// (per-file-limited) size to the selection budget — never its full length.
/// </summary>
public sealed record ContextContentPolicy
{
    /// <summary>Max files included in any single context selection (<c>Evidence:Context:MaximumFiles</c>).</summary>
    public int MaxFiles { get; init; } = 200;

    /// <summary>Max characters across a single context selection (<c>Evidence:Context:MaximumCharacters</c>).</summary>
    public int MaxCharacters { get; init; } = 500_000;

    /// <summary>Max characters rendered for any single file (<c>Evidence:Context:MaximumCharactersPerFile</c>).</summary>
    public int MaxCharactersPerFile { get; init; } = 8_000;

    /// <summary>
    /// The effective characters a file contributes to the context budget — the
    /// size that will actually be rendered. Budgeting and rendering both use this
    /// single rule, so <see cref="AnalysisContextSelection.EstimatedContentSize"/>
    /// matches what the renderer produces.
    /// </summary>
    public int EffectiveContextCharacters(ScannedFile file)
        => Math.Min(file.Content?.Length ?? 0, MaxCharactersPerFile);
}
