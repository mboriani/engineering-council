namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The deterministic repository-context selection for one acquisition step: which
/// files were chosen (for a discipline, or the whole repo), by which strategy,
/// and why. Rule-based — no embeddings, no vector search.
/// </summary>
public sealed record AnalysisContextSelection
{
    /// <summary>Name of the selection strategy used (e.g. "architecture-v1", "repository-wide").</summary>
    public required string Strategy { get; init; }

    /// <summary>The files selected as context.</summary>
    public required IReadOnlyList<ScannedFile> Files { get; init; }

    public required int TotalRepositoryFiles { get; init; }

    public required int SelectedFileCount { get; init; }

    /// <summary>
    /// Sum of the selected files' EFFECTIVE content lengths (characters) — each
    /// file counts only what the context renderer will actually produce (its
    /// per-file-limited size), so this matches the rendered context.
    /// </summary>
    public required long EstimatedContentSize { get; init; }

    /// <summary>Human-readable reasons/signals behind the selection and any exclusions.</summary>
    public IReadOnlyList<string> SelectionReasons { get; init; } = [];
}
