using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// A specialized analyzer agent for a single engineering discipline. As of
/// Milestone 007 it reasons over normalized <see cref="EngineeringObservation"/>s
/// (not raw provider evidence): it selects observations relevant to its
/// discipline, correlates them, applies discipline-specific reasoning, and
/// produces <see cref="Finding"/>s — preserving provenance back to the
/// observations, providers, rules and files.
///
/// It ONLY analyzes — it never acquires evidence, writes files, or renders reports.
/// </summary>
public interface IAnalyzerAgent
{
    /// <summary>Identifier recorded on each finding's <see cref="Finding.SourceAgent"/>.</summary>
    string Name { get; }

    /// <summary>The engineering discipline this analyzer specializes in.</summary>
    FindingCategory Category { get; }

    Task<IReadOnlyList<Finding>> AnalyzeAsync(
        IReadOnlyList<EngineeringObservation> observations,
        AnalyzerContext context,
        CancellationToken cancellationToken = default);
}
