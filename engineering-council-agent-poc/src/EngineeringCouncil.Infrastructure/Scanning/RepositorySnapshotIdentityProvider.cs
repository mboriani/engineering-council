using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Scanning;

/// <summary>
/// Default repository snapshot identity provider (Milestone 015.3C). Combines the
/// read-only Git metadata (<see cref="GitSnapshotMetadataProvider"/>) with the
/// deterministic snapshot fingerprint computed over the SAME scanned file
/// selection the Council analyzes. The end-of-run verification re-scans with the
/// existing scanner (reusing the identical file-selection rules — there is no
/// second independent definition of "repository files"), recomputes the
/// fingerprint and compares it to the start value. It never modifies the target.
/// </summary>
public sealed class RepositorySnapshotIdentityProvider : IRepositorySnapshotIdentityProvider
{
    private readonly IRepositoryScanner _scanner;
    private readonly string _gitExecutable;

    public RepositorySnapshotIdentityProvider(IRepositoryScanner scanner, string gitExecutable = "git")
    {
        _scanner = scanner;
        _gitExecutable = gitExecutable;
    }

    public Task<RepositorySnapshotIdentity> CaptureAsync(
        RepositorySnapshot snapshot,
        string rootPath,
        ScanOptions options,
        CancellationToken cancellationToken = default)
    {
        var git = GitSnapshotMetadataProvider.Read(rootPath, _gitExecutable);
        return Task.FromResult(new RepositorySnapshotIdentity
        {
            VersionControl = git is null ? null : "git",
            CommitSha = git?.CommitSha,
            Branch = git?.Branch,
            IsDirty = git?.IsDirty,
            HasUntrackedFiles = git?.HasUntrackedFiles,
            SnapshotFingerprint = SnapshotFingerprint.Compute(rootPath, snapshot.Files),
            RepositoryChangedDuringRun = false
        });
    }

    public async Task<bool> ChangedDuringRunAsync(
        string rootPath,
        ScanOptions options,
        string startFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(startFingerprint))
            return false;

        // Content is not needed for identity — only the file selection and its
        // bytes. LoadContent=false reuses the exact same selection rules while
        // avoiding re-loading up to MaxFilesWithContent file bodies.
        var endScanOptions = options with { LoadContent = false };
        var endSnapshot = await _scanner
            .ScanAsync(rootPath, endScanOptions, cancellationToken)
            .ConfigureAwait(false);
        var endFingerprint = SnapshotFingerprint.Compute(rootPath, endSnapshot.Files);
        return !string.Equals(endFingerprint, startFingerprint, StringComparison.Ordinal);
    }
}