namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// One invocation of the Claude Code executable, described without any shell string.
/// <see cref="Executable"/> is the process file name; <see cref="WorkingDirectory"/>
/// is the analyzed repository root (the agent explores that repository itself);
/// <see cref="Arguments"/> are passed safely via <c>ProcessStartInfo.ArgumentList</c>;
/// <see cref="Timeout"/> is enforced by the runner before the process is terminated.
/// </summary>
public sealed record ClaudeCodeProcessRequest
{
    public required string Executable { get; init; }

    public required string WorkingDirectory { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required TimeSpan Timeout { get; init; }
}

/// <summary>
/// The bounded outcome of one Claude Code execution. On success, <see cref="StandardOutput"/>
/// holds the structured JSON envelope the CLI produced. Streams are bounded and safe to
/// surface as diagnostics; they never contain secrets, environment variables, or full
/// machine details. <see cref="TimedOut"/> is true only when the runner's own timeout
/// fired; a run/user cancellation instead surfaces as an
/// <see cref="OperationCanceledException"/> after the process is terminated.
/// </summary>
public sealed record ClaudeCodeProcessResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public TimeSpan Duration { get; init; }

    public bool TimedOut { get; init; }
}

/// <summary>
/// The minimal process-execution seam for the Claude Code agentic runtime (Milestone
/// 013.5). Its only job is to start the executable, set the working directory,
/// provide the analysis instruction, capture bounded stdout/stderr, capture the
/// exit code, honor cancellation, and enforce a timeout. It is deliberately NOT a
/// generic shell or command executor — this milestone needs Claude Code only. The
/// process lifecycle (whole-tree termination on timeout/cancellation) follows the
/// lessons verified during M13.3A.
/// </summary>
public interface IClaudeCodeProcessRunner
{
    Task<ClaudeCodeProcessResult> RunAsync(
        ClaudeCodeProcessRequest request,
        CancellationToken cancellationToken = default);
}
