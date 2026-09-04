namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for the OpenCode agentic evidence source (Milestone 013.2, extended
/// Milestone 013.3). Bound from the <c>Evidence:OpenCode</c> section. OpenCode is an
/// EXTERNAL runtime: the council launches it against the repository, gives it one
/// discipline-specific read-only analysis instruction, and converts its structured
/// output into the same <c>Evidence</c> abstraction as every other source. It stays
/// DISABLED by default — the offline Mock remains the zero-config default and nothing
/// is executed unless the provider is selected explicitly.
///
/// Milestone 013.3 adds OPTIONAL explicit model selection: <see cref="Model"/> is the
/// OpenCode model identifier in OpenCode's <c>provider/model</c> format (e.g.
/// <c>deepseek/deepseek-v4-flash</c>) and is passed through the existing safe process
/// invocation as a <c>--model</c> argument. When unset, OpenCode uses its own runtime
/// default — existing M13.2 behavior. The model is configuration, never domain logic,
/// and is NOT hardcoded anywhere; no API key or credential is ever configured here
/// (credentials belong to OpenCode's own auth/env mechanism, e.g.
/// <c>DEEPSEEK_API_KEY</c>, and never reach the Council).
/// </summary>
public sealed class OpenCodeOptions
{
    public const string SectionName = "Evidence:OpenCode";

    /// <summary>Provider display name (also the acquisition provider name).</summary>
    public string ProviderName => "OpenCode";

    /// <summary>Disabled by default: OpenCode is an external runtime that reads the repository.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The OpenCode executable (a name on PATH, or an absolute path). Defaults to
    /// <c>opencode</c>. Never concatenated into a shell string — it is passed via
    /// <c>ProcessStartInfo.ArgumentList</c> with <c>UseShellExecute = false</c>.
    /// </summary>
    public string Executable { get; set; } = "opencode";

    /// <summary>
    /// Optional OpenCode model identifier in OpenCode's <c>provider/model</c> format
    /// (e.g. <c>deepseek/deepseek-v4-flash</c>), passed to the executable as
    /// <c>--model &lt;id&gt;</c> via <c>ArgumentList</c> (never a shell string).
    /// Empty/null means OpenCode uses its own configured/default model — the model is
    /// never mandatory and the provider never guesses or hardcodes one.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Optional OpenCode port for the run's LOCAL server, passed to the executable as
    /// <c>--port &lt;n&gt;</c> (or bare <c>--port</c> for a random port) via
    /// <c>ArgumentList</c> (never a shell string). When set (≥0), the run starts its own
    /// local server instead of competing for the shared OpenCode server that an
    /// interactive session may already hold — a busy shared server can otherwise make
    /// <c>opencode run</c> queue until the 120s timeout (M14.1). Unset (null) means
    /// OpenCode uses its default server discovery. <c>0</c> means a bare <c>--port</c>
    /// (OpenCode picks a random free port).
    /// </summary>
    public int? Port { get; set; }

    /// <summary>Per-execution timeout before the process is terminated (existing-style default).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>True when the provider is enabled and has an executable configured.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Executable);

    /// <summary>A secret-safe explanation of why the provider cannot run, or null when it can.</summary>
    public string? UnavailableReason
        => !Enabled
            ? "OpenCode is disabled (set Evidence:OpenCode:Enabled = true, or select it explicitly)."
            : string.IsNullOrWhiteSpace(Executable)
                ? "OpenCode has no executable configured (set Evidence:OpenCode:Executable)."
                : null;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 120 : TimeoutSeconds);
}
