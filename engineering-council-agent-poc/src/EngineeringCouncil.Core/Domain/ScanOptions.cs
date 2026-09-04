namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Options controlling the read-only scan. Defaults encode the required
/// ignore list and safe limits so a scan never becomes expensive by accident.
/// </summary>
public sealed record ScanOptions
{
    /// <summary>Directory names ignored anywhere in the tree.</summary>
    public IReadOnlySet<string> IgnoredDirectories { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", "node_modules", "packages", "artifacts", ".vs"
        };

    /// <summary>File extensions whose content is loaded for analysis.</summary>
    public IReadOnlySet<string> IncludedExtensions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".sln", ".props", ".targets",
            ".json", ".yml", ".yaml", ".razor", ".cshtml", ".md"
        };

    /// <summary>Files larger than this are catalogued but their content is not loaded.</summary>
    public long MaxFileSizeBytesForContent { get; init; } = 256 * 1024;

    /// <summary>Upper bound on files whose content is loaded (protects against huge repos).</summary>
    public int MaxFilesWithContent { get; init; } = 400;

    /// <summary>
    /// When false, the scan enumerates and stats the same post-ignore-rule file
    /// selection but does not load any file bodies (Milestone 015.3C: the end-of-run
    /// identity verification reuses the exact same selection rules without paying
    /// the content-loading cost). The selection is identical either way.
    /// </summary>
    public bool LoadContent { get; init; } = true;
}
