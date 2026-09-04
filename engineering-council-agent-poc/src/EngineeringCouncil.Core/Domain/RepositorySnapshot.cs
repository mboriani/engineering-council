namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The read-only result of scanning a repository: the files that survived the
/// ignore rules plus lightweight aggregate metadata used to build the LLM context.
/// </summary>
public sealed record RepositorySnapshot
{
    public required string RootPath { get; init; }

    public required string SolutionName { get; init; }

    /// <summary>Git branch, when the root is a git repository.</summary>
    public string? Branch { get; init; }

    /// <summary>Git commit sha, when the root is a git repository.</summary>
    public string? Commit { get; init; }

    public IReadOnlyList<ScannedFile> Files { get; init; } = [];

    /// <summary>Relative paths of discovered .sln files.</summary>
    public IReadOnlyList<string> SolutionFiles { get; init; } = [];

    /// <summary>Relative paths of discovered .csproj files.</summary>
    public IReadOnlyList<string> ProjectFiles { get; init; } = [];

    /// <summary>Count of files per extension (after ignore rules).</summary>
    public IReadOnlyDictionary<string, int> FileCountByExtension { get; init; }
        = new Dictionary<string, int>();

    public int TotalFiles => Files.Count;

    public long TotalSizeBytes { get; init; }
}
