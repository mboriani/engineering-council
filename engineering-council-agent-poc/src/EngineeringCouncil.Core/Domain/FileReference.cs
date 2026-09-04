namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A pointer to a location in the analyzed repository that backs a finding.
/// Paths are repository-relative so outputs stay portable.
/// </summary>
public sealed record FileReference
{
    /// <summary>Repository-relative path, using forward slashes.</summary>
    public required string Path { get; init; }

    /// <summary>Optional 1-based start line the finding refers to.</summary>
    public int? StartLine { get; init; }

    /// <summary>Optional 1-based end line the finding refers to.</summary>
    public int? EndLine { get; init; }

    /// <summary>Optional short note about why this location is relevant.</summary>
    public string? Note { get; init; }
}
