using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Shared discipline-registration validation (Milestone 011.2, issue A6). A
/// requested discipline with no registered <c>IAnalyzerAgent</c> must fail fast —
/// BEFORE any repository scan, provider execution, or package generation —
/// reporting ALL unsupported disciplines. Used consistently by the CLI, the API,
/// and the <see cref="AnalysisPipeline"/> entry so every entry point behaves the
/// same way.
/// </summary>
public static class AnalyzerDisciplineValidator
{
    /// <summary>Valid-enum disciplines that have no registered analyzer but are valid to request.</summary>
    public static IReadOnlyList<FindingCategory> Unsupported(
        IReadOnlyList<FindingCategory>? requested,
        IReadOnlyList<FindingCategory> registered)
    {
        if (requested is null || requested.Count == 0 || registered.Count == 0)
            return [];

        var registeredSet = registered.ToHashSet();
        return requested.Where(d => !registeredSet.Contains(d)).Distinct().ToList();
    }

    /// <summary>Throws <see cref="UnsupportedDisciplineException"/> listing every unsupported discipline.</summary>
    public static void EnsureSupported(
        IReadOnlyList<FindingCategory>? requested,
        IReadOnlyList<FindingCategory> registered)
    {
        var unsupported = Unsupported(requested, registered);
        if (unsupported.Count > 0)
            throw new UnsupportedDisciplineException(unsupported);
    }

    public static string FormatUnsupported(IReadOnlyList<FindingCategory> unsupported)
        => unsupported.Count switch
        {
            1 => $"Requested discipline '{unsupported[0]}' has no registered analyzer.",
            _ => $"Requested disciplines {string.Join(", ", unsupported.Select(d => $"'{d}'"))} have no registered analyzer."
        };
}

/// <summary>
/// Thrown when one or more requested disciplines have no registered analyzer agent.
/// Carries the full unsupported set so callers can report all of them at once.
/// </summary>
public sealed class UnsupportedDisciplineException : Exception
{
    public IReadOnlyList<FindingCategory> Unsupported { get; }

    public UnsupportedDisciplineException(IReadOnlyList<FindingCategory> unsupported)
        : base(AnalyzerDisciplineValidator.FormatUnsupported(unsupported))
        => Unsupported = unsupported;
}
