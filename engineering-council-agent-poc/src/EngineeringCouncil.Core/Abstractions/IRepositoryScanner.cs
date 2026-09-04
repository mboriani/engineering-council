using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Reads a target repository from local disk in READ-ONLY mode and produces a
/// <see cref="RepositorySnapshot"/>. Implementations must never write to,
/// modify, or delete anything under the scanned path.
/// </summary>
public interface IRepositoryScanner
{
    Task<RepositorySnapshot> ScanAsync(
        string rootPath,
        ScanOptions options,
        CancellationToken cancellationToken = default);
}
