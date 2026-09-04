using System.Diagnostics;
using EngineeringCouncil.Infrastructure.Evidence;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 013.4 — focused REAL process tests for <see cref="CodexProcessRunner"/>.
/// They need no Codex installation: they launch the dotnet CLI (always present for
/// the build) and, on Windows only, <c>ping</c> / <c>cmd</c> to prove timeout and
/// cancellation actually terminate a long-running child AND its complete process tree
/// (the M13.3A lesson). Everything else in the suite uses a fake runner.
///
/// These classes share a NON-PARALLEL collection with the OpenCode runner tests:
/// both spawn short-lived <c>cmd /c ping</c> trees, and running them concurrently
/// would make one test's orphan-survivor check see the other test's still-running
/// child.
/// </summary>
[Collection(ProcessRunnerTestsShared.CollectionName)]
public sealed class CodexProcessRunnerTests : IDisposable
{
    private readonly string _workingDir = Path.Combine(Path.GetTempPath(), "ec-codex-cwd-" + Guid.NewGuid().ToString("N"));

    public CodexProcessRunnerTests()
    {
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CodexProcessRunner_captures_stdout_and_exit_code()
    {
        var runner = new CodexProcessRunner();

        var result = await runner.RunAsync(new CodexProcessRequest
        {
            Executable = DotNetExecutable,
            WorkingDirectory = _workingDir,
            Arguments = ["--version"],
            Timeout = TimeSpan.FromSeconds(30)
        });

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains(".", result.StandardOutput.Trim(), StringComparison.Ordinal); // a version like 10.0.x
    }

    [Fact]
    public async Task CodexProcessRunner_enforces_timeout_and_terminates_the_process()
    {
        if (!OperatingSystem.IsWindows()) return; // ping is a Windows fixture

        var runner = new CodexProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(new CodexProcessRequest
        {
            Executable = "ping",
            WorkingDirectory = _workingDir,
            Arguments = ["127.0.0.1", "-n", "30"],   // would run ~30s if never terminated
            Timeout = TimeSpan.FromSeconds(2)
        });
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);          // killed, not exited
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"termination took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task CodexProcessRunner_propagates_cancellation_and_terminates_the_process()
    {
        if (!OperatingSystem.IsWindows()) return; // ping is a Windows fixture

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var runner = new CodexProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new CodexProcessRequest
        {
            Executable = "ping",
            WorkingDirectory = _workingDir,
            Arguments = ["127.0.0.1", "-n", "30"],   // would run ~30s if never terminated
            Timeout = TimeSpan.FromSeconds(60)      // the RUN cancellation must win, not the timeout
        }, cts.Token));
        stopwatch.Stop();

        // Cancellation propagated (not converted into a timed-out/ordinary result) and the
        // child was terminated promptly rather than left running.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"cancellation took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task CodexProcessRunner_timeout_terminates_the_complete_process_tree()
    {
        // M13.3A lesson: a timeout must terminate the WHOLE process tree, not just the
        // directly-started process. `cmd /c ping -n 30` runs ~30s and spawns `ping` as a
        // child — if only the parent were killed, the ping grandchild would survive as an
        // orphan. Snapshot existing ping PIDs so we only count new ones.
        if (!OperatingSystem.IsWindows()) return; // cmd/ping are Windows fixtures

        var preExisting = ExistingProcessIds("ping");
        var runner = new CodexProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(new CodexProcessRequest
        {
            Executable = "cmd",
            WorkingDirectory = _workingDir,
            Arguments = ["/c", "ping 127.0.0.1 -n 30"],
            Timeout = TimeSpan.FromSeconds(2)
        });
        stopwatch.Stop();

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);          // killed, not exited
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"termination took {stopwatch.Elapsed}");

        await Task.Delay(500);                      // give the tree kill a moment to settle
        var survivors = NewProcessIds("ping", preExisting);
        Assert.Empty(survivors);                    // no orphaned grandchild left running
    }

    [Fact]
    public async Task CodexProcessRunner_cancellation_terminates_the_complete_process_tree()
    {
        // M13.3A lesson: run cancellation must terminate the whole process tree too
        // (parent `cmd` plus its child `ping`), never leaving orphans.
        if (!OperatingSystem.IsWindows()) return; // cmd/ping are Windows fixtures

        var preExisting = ExistingProcessIds("ping");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var runner = new CodexProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new CodexProcessRequest
        {
            Executable = "cmd",
            WorkingDirectory = _workingDir,
            Arguments = ["/c", "ping 127.0.0.1 -n 30"],
            Timeout = TimeSpan.FromSeconds(60)      // the RUN cancellation must win, not the timeout
        }, cts.Token));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"cancellation took {stopwatch.Elapsed}");

        await Task.Delay(500);                      // give the tree kill a moment to settle
        var survivors = NewProcessIds("ping", preExisting);
        Assert.Empty(survivors);                    // no orphaned grandchild left running
    }

    private static HashSet<int> ExistingProcessIds(string name)
        => Process.GetProcessesByName(name).Select(p => p.Id).ToHashSet();

    private static IReadOnlyList<int> NewProcessIds(string name, HashSet<int> preExisting)
        => Process.GetProcessesByName(name)
            .Select(p => p.Id)
            .Where(id => !preExisting.Contains(id))
            .ToList();

    private static string DotNetExecutable
        => Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
            : "dotnet";
}
