using System.ComponentModel;
using System.Diagnostics;

namespace EngineeringCouncil.Infrastructure.Scanning;

/// <summary>
/// Best-effort, read-only reader of a repository's current branch and commit by
/// parsing the <c>.git</c> directory directly (no git executable needed). Returns
/// nulls when the target is not a git working tree or cannot be read.
/// </summary>
public static class GitProbe
{
    public static (string? Branch, string? Commit) Probe(string rootPath)
    {
        try
        {
            var gitDir = Path.Combine(rootPath, ".git");
            if (!Directory.Exists(gitDir)) return (null, null);

            var headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath)) return (null, null);

            var head = File.ReadAllText(headPath).Trim();

            // Detached HEAD: HEAD contains a raw commit sha.
            if (!head.StartsWith("ref:", StringComparison.Ordinal))
                return (null, ShortSha(head));

            // Symbolic ref: "ref: refs/heads/<branch>".
            var refName = head[4..].Trim();
            var branch = refName.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? refName["refs/heads/".Length..]
                : refName;

            var commit = ResolveRef(gitDir, refName);
            return (branch, commit is null ? null : ShortSha(commit));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Returns true when the Git working tree has no tracked modifications
    /// relative to HEAD. Uses `git status --porcelain --untracked-files=no`.
    /// Returns false on error or when git is not available.
    /// </summary>
    public static bool IsCleanWorkingTree(string rootPath)
    {
        try
        {
            var gitDir = Path.Combine(rootPath, ".git");
            if (!Directory.Exists(gitDir)) return false;

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status --porcelain --untracked-files=no",
                    WorkingDirectory = rootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 && string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the list of tracked file paths with modifications relative to HEAD.
    /// Uses `git status --porcelain --untracked-files=no`. Returns empty on error.
    /// </summary>
    public static IReadOnlyList<string> GetModifiedTrackedFiles(string rootPath)
    {
        try
        {
            var gitDir = Path.Combine(rootPath, ".git");
            if (!Directory.Exists(gitDir)) return [];

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status --porcelain --untracked-files=no",
                    WorkingDirectory = rootPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0) return [];

            var modified = new List<string>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 4) continue;
                var path = line[3..].Trim('"');
                modified.Add(path.Replace('\\', '/'));
            }

            return modified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return [];
        }
    }

    private static string? ResolveRef(string gitDir, string refName)
    {
        var loose = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose))
            return File.ReadAllText(loose).Trim();

        // Fall back to packed-refs.
        var packed = Path.Combine(gitDir, "packed-refs");
        if (!File.Exists(packed)) return null;

        foreach (var line in File.ReadLines(packed))
        {
            if (line.Length == 0 || line[0] is '#' or '^') continue;
            var parts = line.Split(' ', 2);
            if (parts.Length == 2 && parts[1].Trim() == refName)
                return parts[0].Trim();
        }

        return null;
    }

    private static string ShortSha(string sha)
        => sha.Length >= 12 ? sha[..12] : sha;
}
