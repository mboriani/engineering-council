using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Captures and verifies the repository snapshot identity for a run (Milestone
/// 015.3C): the VCS facts and the deterministic snapshot fingerprint of the
/// analyzed working state, captured BEFORE acquisition begins, plus a single
/// end-of-acquisition verification that mid-run repository mutation is surfaced
/// without failing the run or replacing the start identity.
/// </summary>
public interface IRepositorySnapshotIdentityProvider
{
    /// <summary>
    /// Captures the identity of the repository state the providers are about to
    /// analyze: read-only VCS metadata plus the snapshot fingerprint over the
    /// scanned (post-ignore-rule) file selection. Must be called BEFORE acquisition.
    /// </summary>
    Task<RepositorySnapshotIdentity> CaptureAsync(
        RepositorySnapshot snapshot,
        string rootPath,
        ScanOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the snapshot fingerprint once at the end of the run and reports
    /// whether it differs from the start fingerprint. The start fingerprint is
    /// never replaced; this only surfaces a detected mutation.
    /// </summary>
    Task<bool> ChangedDuringRunAsync(
        string rootPath,
        ScanOptions options,
        string startFingerprint,
        CancellationToken cancellationToken = default);
}