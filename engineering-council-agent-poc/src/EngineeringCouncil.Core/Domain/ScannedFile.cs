namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A single source file discovered by the read-only scanner.
/// Content is loaded lazily/optionally to keep large repos manageable.
/// </summary>
public sealed record ScannedFile
{
    /// <summary>Repository-relative path using forward slashes.</summary>
    public required string RelativePath { get; init; }

    /// <summary>File extension (lower-case, including the dot), e.g. ".cs".</summary>
    public required string Extension { get; init; }

    public long SizeBytes { get; init; }

    public int LineCount { get; init; }

    /// <summary>File content, when loaded. May be null for skipped/binary/oversized files.</summary>
    public string? Content { get; init; }
}
