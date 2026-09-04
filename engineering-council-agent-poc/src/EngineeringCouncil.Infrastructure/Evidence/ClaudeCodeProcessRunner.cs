using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using EngineeringCouncil.Infrastructure.Llm;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Launches the Claude Code executable against the analyzed repository root. Safety
/// properties (Milestone 013.5), matching the OpenCode (M13.2/M13.3) and Codex
/// (M13.4) runners:
///
/// - NO shell string execution: <c>UseShellExecute = false</c> and
///   <c>ArgumentList</c> are used, so neither the repository path nor the analysis
///   instruction is ever concatenated into a <c>cmd /c</c> / <c>sh -c</c> string.
/// - The process runs with <c>WorkingDirectory</c> = repository root (the agent
///   explores that repository itself; nothing is copied or sent through stdin).
/// - stdout is captured (bounded) as the structured result; stderr is captured
///   (bounded) as a diagnostic. No secrets, environment variables, or machine
///   details are exposed.
/// - An internal timeout terminates the WHOLE process tree and reports
///   <see cref="ClaudeCodeProcessResult.TimedOut"/>.
/// - A run/user cancellation terminates the WHOLE process tree and then propagates
///   as <see cref="OperationCanceledException"/> — it is never converted into an
///   ordinary failure, and no orphan child is left running (M13.3A lesson).
/// - A process that cannot start (missing executable, bad working directory) becomes
///   a categorized <see cref="LlmErrorCategory.ProcessError"/> failure.
/// </summary>
public sealed class ClaudeCodeProcessRunner : IClaudeCodeProcessRunner
{
    /// <summary>Bounded capture for the structured stdout (must fit the evidence schema).</summary>
    private const int MaxOutputCharacters = 128 * 1024;

    /// <summary>Bounded stderr diagnostic — never persisted unlimited or as raw tool output.</summary>
    private const int MaxErrorCharacters = 8 * 1024;

    public async Task<ClaudeCodeProcessResult> RunAsync(
        ClaudeCodeProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.StartNew();
        using var process = Start(request);

        // Start the bounded reads BEFORE waiting so an over-productive child can
        // never deadlock on a full pipe buffer.
        var stdout = ReadBoundedAsync(process.StandardOutput, MaxOutputCharacters, cancellationToken);
        var stderr = ReadBoundedAsync(process.StandardError, MaxErrorCharacters, cancellationToken);

        // The runner's OWN timeout (distinct from a run/user cancellation).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

        var timedOut = false;
        var exitCode = -1;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Run/user cancellation: terminate the process and propagate as cancellation
            // (never as an ordinary provider failure, never left running intentionally).
            Kill(process);
            await DrainQuietlyAsync(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            // The runner's own timeout fired: terminate and report a timed-out result.
            timedOut = true;
            Kill(process);
            await DrainQuietlyAsync(stdout, stderr).ConfigureAwait(false);
        }

        var (standardOutput, standardError) = await DrainQuietlyAsync(stdout, stderr).ConfigureAwait(false);
        startedAt.Stop();

        return new ClaudeCodeProcessResult
        {
            ExitCode = timedOut ? -1 : exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Duration = startedAt.Elapsed,
            TimedOut = timedOut
        };
    }

    private static Process Start(ClaudeCodeProcessRequest request)
    {
        var info = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.WorkingDirectory
        };
        foreach (var argument in request.Arguments)
            info.ArgumentList.Add(argument);

        try
        {
            return Process.Start(info) ?? throw new InvalidOperationException("The process was not started.");
        }
        catch (Win32Exception ex)
        {
            throw new LlmProviderException(LlmErrorCategory.ProcessError,
                $"Claude Code could not be started ('{request.Executable}' in '{request.WorkingDirectory}'). "
                + "Verify the executable is installed and on PATH, and that the working directory exists.",
                ex);
        }
    }

    /// <summary>Terminates the process (and its tree) best-effort; never throws.</summary>
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* already exited */ }
        catch (Win32Exception) { /* already exited / not killable */ }
        catch (NotSupportedException) { /* not running */ }
    }

    /// <summary>
    /// Reads to EOF but retains at most <paramref name="maxChars"/> characters, so the
    /// pipe never deadlocks and memory stays bounded even if the child is verbose.
    /// </summary>
    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var retained = new StringBuilder(Math.Min(maxChars, 1024));

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            if (retained.Length < maxChars)
            {
                var take = Math.Min(read, maxChars - retained.Length);
                retained.Append(buffer, 0, take);
            }
        }

        return retained.ToString();
    }

    /// <summary>Drains both captured streams to EOF so no pipe deadlock survives a kill.</summary>
    private static async Task<(string StdOut, string StdErr)> DrainQuietlyAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* run token cancelled while draining; process already terminated */ }
        catch (ObjectDisposedException) { /* process disposed while a read was in flight */ }

        return (stdout.Status == TaskStatus.RanToCompletion ? stdout.Result : string.Empty,
                stderr.Status == TaskStatus.RanToCompletion ? stderr.Result : string.Empty);
    }
}
