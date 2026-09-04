using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Shared context passed to evidence interpreters and analyzers: the read-only
/// repository snapshot (e.g. to validate file references) plus run identity.
/// </summary>
public sealed record AnalyzerContext
{
    public required RepositorySnapshot Snapshot { get; init; }

    public string RunId { get; init; } = string.Empty;
}
