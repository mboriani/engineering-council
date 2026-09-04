using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Persists analysis runs and their output artifacts. The v1 implementation
/// writes to the local <c>outputs/{runId}/</c> folder; the contract allows a
/// database-backed store later without touching callers.
/// </summary>
public interface IAnalysisRunRepository
{
    /// <summary>
    /// Persists the run and the Engineering Review Package (plus their rendered
    /// artifacts). Returns the absolute path of the run's output directory.
    /// </summary>
    Task<string> SaveAsync(
        AnalysisRun run,
        EngineeringReviewPackage package,
        CancellationToken cancellationToken = default);

    Task<AnalysisRun?> GetAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken = default);
}
