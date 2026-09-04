using System.ComponentModel;
using System.Diagnostics;

namespace EngineeringCouncil.Infrastructure.Scanning;

/// <summary>
/// Read-only VCS facts about the target working tree (Milestone 015.3C).
/// Nulls mean "unavailable": non-Git repository, detached HEAD, or git itself
/// could not be invoked. Git is NEVER used to modify the repository.
/// </summary>
public sealed record GitSnapshotMetadata
{
    public string? CommitSha { get; init; }
    public string? Branch { get; init; }
    public bool? IsDirty { get; init; }
    public bool? HasUntrackedFiles { get; init; }
}

/// <summary>
/// Best-effort, strictly READ-ONLY reader of Git identity metadata. Invokes the
/// git executable via <c>ProcessStartInfo.ArgumentList</c> (argv, never a shell
/// string) with <c>UseShellExecute = false</c> — the same safety rules as the
/// evidence process runners. Only the commands the milestone prescribes are used:
/// <c>rev-parse</c>, <c>symbolic-ref</c>, <c>status --porcelain</c>. It never runs
/// checkout/reset/stash/clean/add/commit and never modifies the target repository.
/// When git is unavailable the reader degrades gracefully to nulls — it never
/// fails an analysis run.
/// </summary>
public static class GitSnapshotMetadataProvider
{
    /// <summary>Max time a read-only git invocation may take before it is killed.</summary>
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the metadata. Returns null when the target is positively NOT a git
    /// working tree. Returns an all-null <see cref="GitSnapshotMetadata"/> when
    /// git itself is unavailable but a <c>.git</c> directory positively identifies
    /// the tree as Git (identity beyond that cannot be determined).
    /// </summary>
    public static GitSnapshotMetadata? Read(string rootPath, string gitExecutable = "git")
    {
        var workTree = TryRun(gitExecutable, rootPath, "rev-parse", "--is-inside-work-tree");
        if (workTree is null || workTree.Value.ExitCode != 0
            || !string.Equals(workTree.Value.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            // Not positively a git work tree via git. Positive identification
            // without git: a .git directory.
            return Directory.Exists(Path.Combine(rootPath, ".git"))
                ? new GitSnapshotMetadata()
                : null;
        }

        var commit = TryRun(gitExecutable, rootPath, "rev-parse", "HEAD");
        var commitSha = commit is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(commit.Value.StandardOutput)
            ? commit.Value.StandardOutput.Trim()
            : null;

        // symbolic-ref fails (nonzero) on a detached HEAD → branch null.
        var branchRun = TryRun(gitExecutable, rootPath, "symbolic-ref", "--short", "HEAD");
        var branch = branchRun is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(branchRun.Value.StandardOutput)
            ? branchRun.Value.StandardOutput.Trim()
            : null;

        bool? isDirty = null;
        bool? hasUntracked = null;
        var status = TryRun(gitExecutable, rootPath, "status", "--porcelain");
        if (status is { ExitCode: 0 })
        {
            isDirty = false;
            hasUntracked = false;
            foreach (var line in status.Value.StandardOutput.Split('\n'))
            {
                if (line.Length < 3) continue;
                var x = line[0];
                var y = line[1];
                if (x == '?' && y == '?')
                    hasUntracked = true;
                else if (x != ' ' || y != ' ')
                    isDirty = true;
            }
        }

        return new GitSnapshotMetadata
        {
            CommitSha = commitSha,
            Branch = branch,
            IsDirty = isDirty,
            HasUntrackedFiles = hasUntracked
        };
    }

    private static (int ExitCode, string StandardOutput)? TryRun(
        string gitExecutable, string workingDirectory, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = gitExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return null;

            // Read stdout synchronously; git output for these commands is tiny
            // (a SHA, a branch name, a status listing), so no pipe deadlock.
            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit((int)GitTimeout.TotalMilliseconds))
            {
                Kill(process);
                return null;
            }
            return (process.ExitCode, stdout);
        }
        catch (Win32Exception)
        {
            return null; // executable missing / not launchable → git unavailable
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already exited */ }
        catch (Win32Exception) { /* already exited / not killable */ }
        catch (NotSupportedException) { /* not running */ }
    }
}