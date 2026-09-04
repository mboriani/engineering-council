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
