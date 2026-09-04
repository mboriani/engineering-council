using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Selects the repository files most relevant to a discipline (or the whole
/// repository when <paramref name="discipline"/> is null). It works only within
/// the already-scanned snapshot (so scanner ignore rules + content limits are
/// respected), preserves enough structure for reasoning, respects configured
/// context limits, and returns a deterministic <see cref="AnalysisContextSelection"/>.
/// Rule-based — no embeddings, no vector search.
/// </summary>
public interface IAnalysisContextSelector
{
    AnalysisContextSelection Select(
        RepositorySnapshot repository,
        FindingCategory? discipline,
        CancellationToken cancellationToken = default);
}
