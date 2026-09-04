namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Lifecycle state of a finding as it passes through the consolidation layer
/// (the <c>IFindingMerger</c>).
/// </summary>
public enum FindingStatus
{
    /// <summary>A standalone finding that was not merged with any other.</summary>
    New = 0,

    /// <summary>A consolidated finding produced by merging two or more raw findings.</summary>
    Merged = 1,

    /// <summary>A raw finding that was folded into another (referenced via <see cref="Finding.DuplicateOf"/>).</summary>
    Duplicate = 2,

    /// <summary>A finding removed from the consolidated set (reserved for the future council).</summary>
    Discarded = 3
}
