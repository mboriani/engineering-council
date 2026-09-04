namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Deterministic, provider-neutral description of the TARGET repository state a
/// run analyzed (Milestone 015.3C). It answers "what exact source state produced
/// this finding?" without ever claiming a commit SHA represents the analyzed
/// source when the working tree differs from that commit.
///
/// The start state is captured BEFORE acquisition begins and is never silently
/// replaced by the end state; <see cref="RepositoryChangedDuringRun"/> surfaces
/// a mid-run mutation (verified once at the end of acquisition).
///
/// Privacy: this record contains only identity metadata (VCS facts and hashes).
/// It never contains file contents, credentials, tokens, connection strings,
/// environment variables, or remote URLs.
/// </summary>
public sealed record RepositorySnapshotIdentity
{
    /// <summary>
    /// Version-control system of the analyzed working tree: <c>"git"</c> when
    /// positively identified, null otherwise. Null for non-Git repositories.
    /// </summary>
    public string? VersionControl { get; init; }

    /// <summary>Full HEAD commit SHA for Git repositories; null otherwise (or when unavailable).</summary>
    public string? CommitSha { get; init; }

    /// <summary>Current branch when available; null for detached HEAD or non-Git repositories.</summary>
    public string? Branch { get; init; }

    /// <summary>
    /// True when the tracked working-tree/index state differs from HEAD; false
    /// when verified clean; null when unavailable (e.g. non-Git or git unavailable).
    /// </summary>
    public bool? IsDirty { get; init; }

    /// <summary>
    /// True when untracked files exist in the analyzed selection; false when
    /// verified absent; null when unavailable (e.g. non-Git or git unavailable).
    /// </summary>
    public bool? HasUntrackedFiles { get; init; }

    /// <summary>
    /// Deterministic identity of the actual analyzed working state — the
    /// post-ignore-rule file selection and its content, not merely HEAD. Changes
    /// when relevant analyzed source content changes (including dirty tracked
    /// and untracked files). Never contains source code or secrets.
    /// </summary>
    public string? SnapshotFingerprint { get; init; }

    /// <summary>
    /// True when the repository state changed between the start-of-acquisition
    /// capture and the single end-of-acquisition verification. The START
    /// fingerprint is always retained; this flag only warns.
    /// </summary>
    public bool RepositoryChangedDuringRun { get; init; }
}