namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for the Codex agentic evidence source (Milestone 013.4). Bound from
/// the <c>Evidence:Codex</c> section. Codex is an EXTERNAL runtime (the OpenAI Codex
/// CLI): the council launches it against the repository, gives it one discipline-
/// specific read-only analysis instruction, and converts its structured output into
/// the same <c>Evidence</c> abstraction as every other source. It stays DISABLED by
/// default — the offline Mock remains the zero-config default and nothing is executed
/// unless the provider is selected explicitly.
///
/// Milestone 013.4: model selection is OPTIONAL configuration
/// (<see cref="Model"/>, e.g. <c>gpt-5.4-mini</c>) passed through the existing safe
/// process invocation as <c>-m &lt;model&gt;</c>. When unset, Codex uses its own
/// configured/default model. The model is configuration, never domain logic, and is
/// NOT hardcoded anywhere. No API key or credential is ever configured here —
/// Codex authenticates through its own login (e.g. <c>codex login</c> / ChatGPT
/// account), which never reaches the Council.
/// </summary>
public sealed class CodexOptions
{
    public const string SectionName = "Evidence:Codex";

    /// <summary>Provider display name (also the acquisition provider name).</summary>
    public string ProviderName => "Codex";

    /// <summary>Disabled by default: Codex is an external runtime that reads the repository.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The Codex executable (a name on PATH, or an absolute path). Defaults to
    /// <c>codex</c>. Never concatenated into a shell string — it is passed via
    /// <c>ProcessStartInfo.ArgumentList</c> with <c>UseShellExecute = false</c>.
    /// </summary>
    public string Executable { get; set; } = "codex";

    /// <summary>
    /// Optional Codex model identifier (e.g. <c>gpt-5.4-mini</c>), passed to the
    /// executable as <c>-m &lt;model&gt;</c> via <c>ArgumentList</c> (never a shell
    /// string). Empty/null means Codex uses its own configured/default model — the
    /// model is never mandatory and the provider never guesses or hardcodes one.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Per-execution timeout before the process is terminated (existing-style default).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>True when the provider is enabled and has an executable configured.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Executable);

    /// <summary>A secret-safe explanation of why the provider cannot run, or null when it can.</summary>
    public string? UnavailableReason
        => !Enabled
            ? "Codex is disabled (set Evidence:Codex:Enabled = true, or select it explicitly)."
            : string.IsNullOrWhiteSpace(Executable)
                ? "Codex has no executable configured (set Evidence:Codex:Executable)."
                : null;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 120 : TimeoutSeconds);
}
