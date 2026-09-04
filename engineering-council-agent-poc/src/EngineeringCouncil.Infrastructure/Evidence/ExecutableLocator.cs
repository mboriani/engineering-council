using System.Text.RegularExpressions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// A resolved process launch that avoids any shell-string routing: <see cref="FileName"/>
/// is a directly launchable executable and <see cref="PrefixArguments"/> (only used when the
/// target is a Node script) are passed before the caller's arguments — all via
/// <c>ProcessStartInfo.ArgumentList</c>, never a <c>cmd /c</c> / <c>sh -c</c> string.
/// </summary>
public readonly record struct ResolvedExecutable(string FileName, IReadOnlyList<string> PrefixArguments);

/// <summary>
/// Resolves a configured executable name to a directly launchable process file.
///
/// Why this exists: <c>Process.Start</c> with <c>UseShellExecute = false</c> on Windows
/// resolves a bare name only to a real <c>.exe</c>. CLI tools installed via npm (OpenCode,
/// Codex, …) put only shims on PATH — <c>&lt;name&gt;.cmd</c>, <c>&lt;name&gt;.ps1</c> — and
/// no <c>&lt;name&gt;.exe</c>, so a bare configured name fails with "cannot find the file".
/// Launching the <c>.cmd</c> shim directly would route the child through <c>cmd.exe</c>,
/// which re-parses the command line and would corrupt/interpret analysis-instruction text
/// (cmd metacharacters such as <c>&amp;</c>, <c>&gt;</c>, <c>%VAR%</c>). This resolver therefore:
///
/// 1. Leaves explicit paths (rooted or relative with a separator) unchanged.
/// 2. For a bare name on Windows, searches PATH (PATHEXT order, real executables preferred).
/// 3. For a found npm-style <c>.cmd</c>/<c>.bat</c> shim, extracts the shim's underlying
///    direct invocation — a native <c>.exe</c> (OpenCode) or a Node script (Codex, launched
///    as <c>node &lt;script&gt; …</c>) — so no shell is ever involved.
/// 4. If nothing resolvable is found, returns the configured name unchanged so the existing
///    actionable <c>ProcessError</c> ("executable not installed / not on PATH") is raised.
///
/// On non-Windows the configured name is returned unchanged: POSIX process spawn already
/// resolves bare names on PATH, including shebang scripts.
/// </summary>
public static class ExecutableLocator
{
    /// <summary>Directly launchable Windows executable extensions (in preference order).</summary>
    private static readonly string[] RealExecutableExtensions = [".com", ".exe"];

    /// <summary>Batch shim extensions npm generates on Windows.</summary>
    private static readonly string[] ShimExtensions = [".cmd", ".bat"];

    /// <summary>Node-script extensions a shim target may end in.</summary>
    private static readonly string[] NodeScriptExtensions = [".js", ".mjs", ".cjs"];

    public static ResolvedExecutable Resolve(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return new ResolvedExecutable(executable, []);

        // POSIX spawn already resolves bare names on PATH, including shebang scripts.
        if (!OperatingSystem.IsWindows())
            return new ResolvedExecutable(executable, []);

        // Explicit path (rooted, or relative with a separator): launch exactly as configured
        // and let Process.Start report any failure with the existing actionable message.
        if (Path.IsPathRooted(executable)
            || executable.IndexOf('\\') >= 0
            || executable.IndexOf('/') >= 0)
            return new ResolvedExecutable(executable, []);

        // Bare name: search PATH the way a developer's shell would, preferring a real
        // executable and resolving shims to their direct target.
        foreach (var directory in PathEntries())
        {
            foreach (var extension in CandidateExtensions())
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (File.Exists(candidate))
                    return ResolveFound(candidate);
            }
        }

        // Not found: unchanged, so the runner surfaces its existing, actionable ProcessError.
        return new ResolvedExecutable(executable, []);
    }

    /// <summary>Resolves a found PATH candidate to a direct launch, unwrapping npm shims.</summary>
    private static ResolvedExecutable ResolveFound(string candidate)
    {
        var extension = Path.GetExtension(candidate);

        if (IsOneOf(extension, RealExecutableExtensions))
            return new ResolvedExecutable(candidate, []);

        if (IsOneOf(extension, ShimExtensions))
        {
            var target = TryExtractNpmShimTarget(candidate);
            if (target is not null)
            {
                var targetExtension = Path.GetExtension(target);

                if (IsOneOf(targetExtension, RealExecutableExtensions))
                    return new ResolvedExecutable(target, []);

                if (IsOneOf(targetExtension, NodeScriptExtensions))
                {
                    var node = ResolveOnPath("node.exe", "node");
                    if (node is not null)
                        return new ResolvedExecutable(node, [target]);
                }
            }

            // Unrecognized shim: fall back to launching it as configured (historical behavior).
            return new ResolvedExecutable(candidate, []);
        }

        return new ResolvedExecutable(candidate, []);
    }

    /// <summary>
    /// Extracts the direct invocation target from an npm <c>cmd-shim</c> batch file: the last
    /// double-quoted path containing <c>node_modules</c>. Returns the fully-expanded target
    /// path, or <c>null</c> if the shim is not in a recognizable npm layout.
    /// </summary>
    private static string? TryExtractNpmShimTarget(string shimPath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(shimPath);
        }
        catch
        {
            return null;
        }

        var shimDirectory = Path.GetDirectoryName(Path.GetFullPath(shimPath));
        if (string.IsNullOrEmpty(shimDirectory))
            return null;

        string? lastTarget = null;
        foreach (var line in lines)
        {
            foreach (Match match in Regex.Matches(line, "\"([^\"]+)\""))
            {
                var value = match.Groups[1].Value;
                if (value.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                    lastTarget = value;
            }
        }

        if (lastTarget is null)
            return null;

        // cmd-shim resolves %dp0% (set from %~dp0) to the shim's own directory at runtime.
        lastTarget = lastTarget
            .Replace("%dp0%", shimDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("%~dp0%", shimDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("%~dp0", shimDirectory, StringComparison.OrdinalIgnoreCase);

        return Path.GetFullPath(lastTarget);
    }

    /// <summary>Resolves one of <paramref name="names"/> to an existing file on PATH.</summary>
    private static string? ResolveOnPath(params string[] names)
    {
        foreach (var directory in PathEntries())
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>PATH directories (empty entries are skipped), split like the shell would.</summary>
    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return entry;
    }

    /// <summary>
    /// Executable extensions to try, in PATHEXT order but with real executables always tried
    /// before batch shims so a direct launch is preferred over any <c>cmd.exe</c> routing.
    /// </summary>
    private static IReadOnlyList<string> CandidateExtensions()
    {
        var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";

        var ordered = new List<string>();
        foreach (var entry in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = entry.StartsWith('.') ? entry : "." + entry;
            if (!ordered.Contains(extension, StringComparer.OrdinalIgnoreCase))
                ordered.Add(extension);
        }

        var real = ordered.Where(e => IsOneOf(e, RealExecutableExtensions));
        var shims = ordered.Where(e => IsOneOf(e, ShimExtensions));
        var rest = ordered.Where(e => !IsOneOf(e, RealExecutableExtensions) && !IsOneOf(e, ShimExtensions));

        return real.Concat(shims).Concat(rest).ToList();
    }

    private static bool IsOneOf(string value, string[] options)
        => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));
}
