namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for the Claude Code agentic evidence source (Milestone 013.5).
/// Bound from the <c>Evidence:ClaudeCode</c> section. Claude Code is an EXTERNAL
/// runtime (the native <c>claude</c> CLI): the council launches it against the
/// repository, gives it one discipline-specific read-only analysis instruction, and
/// converts its structured output into the same <c>Evidence</c> abstraction as every
/// other source. It stays DISABLED by default — the offline Mock remains the
/// zero-config default and nothing is executed unless the provider is selected
/// explicitly.
///
/// Naming (Milestone 013.5): <c>Claude</c> is the DIRECT Anthropic API provider
/// (controlled context, API key, <see cref="LlmProviderOptions"/>); <c>ClaudeCode</c>
/// is this agentic Claude Code CLI runtime. The two are unambiguous providers and
/// must never be silently conflated.
///
/// Authentication belongs to Claude Code's own login (<c>claude auth</c> / the
/// configured account) — the Council never reads, stores, or forwards a Claude
/// credential. Model selection is OPTIONAL configuration
/// (<see cref="Model"/>, e.g. <c>claude-sonnet-5</c>) passed through the existing
/// safe process invocation as <c>--model &lt;model&gt;</c>; when unset, Claude Code
/// uses its own configured/default model. The model is configuration, never domain
/// logic, and is NOT hardcoded anywhere.
/// </summary>
public sealed class ClaudeCodeOptions
{
    public const string SectionName = "Evidence:ClaudeCode";

    /// <summary>Provider display name (also the acquisition provider name).</summary>
    public string ProviderName => "ClaudeCode";

    /// <summary>Disabled by default: Claude Code is an external runtime that reads the repository.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The Claude Code executable (a name on PATH, or an absolute path). Defaults to
    /// <c>claude</c>. Never concatenated into a shell string — it is passed via
    /// <c>ProcessStartInfo.ArgumentList</c> with <c>UseShellExecute = false</c>.
    /// </summary>
    public string Executable { get; set; } = "claude";

    /// <summary>
    /// Optional Claude model identifier (e.g. <c>claude-sonnet-5</c>, an alias like
    /// <c>sonnet</c>/<c>opus</c>, or a full model name), passed to the executable as
    /// <c>--model &lt;id&gt;</c> via <c>ArgumentList</c> (never a shell string).
    /// Empty/null means Claude Code uses its own configured/default model — the model
    /// is never mandatory and the provider never guesses or hardcodes one.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Per-execution timeout before the process is terminated (existing-style default).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>True when the provider is enabled and has an executable configured.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Executable);

    /// <summary>A secret-safe explanation of why the provider cannot run, or null when it can.</summary>
    public string? UnavailableReason
        => !Enabled
            ? "ClaudeCode is disabled (set Evidence:ClaudeCode:Enabled = true, or select it explicitly)."
            : string.IsNullOrWhiteSpace(Executable)
                ? "ClaudeCode has no executable configured (set Evidence:ClaudeCode:Executable)."
                : null;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 120 : TimeoutSeconds);
}
