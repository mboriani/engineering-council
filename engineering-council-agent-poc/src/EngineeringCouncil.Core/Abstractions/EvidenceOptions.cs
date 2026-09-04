using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for how evidence is acquired. Bound from the <c>Evidence</c>
/// configuration section; overridable from the CLI. Scope normally comes from
/// each provider's <see cref="EvidenceProviderMetadata"/>; only overrides are
/// listed here.
/// </summary>
public sealed class EvidenceOptions
{
    public const string SectionName = "Evidence";

    /// <summary>Effective provider names to run (e.g. ["Claude", "Sonar"]).</summary>
    public IReadOnlyList<string> Providers { get; set; } = ["Claude"];

    /// <summary>Optional per-provider acquisition-scope overrides (else provider metadata default).</summary>
    public IReadOnlyDictionary<string, EvidenceAcquisitionScope> ProviderScopes { get; set; }
        = new Dictionary<string, EvidenceAcquisitionScope>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disciplines to analyze; empty = all registered disciplines.</summary>
    public IReadOnlyList<FindingCategory> Disciplines { get; set; } = [];

    /// <summary>Max files included in any single context selection (<c>Evidence:Context:MaximumFiles</c>).</summary>
    public int ContextMaxFiles { get; set; } = 200;

    /// <summary>Max characters across a single context selection (<c>Evidence:Context:MaximumCharacters</c>).</summary>
    public int ContextMaxCharacters { get; set; } = 500_000;

    /// <summary>Max characters rendered for any single file (<c>Evidence:Context:MaximumCharactersPerFile</c>).
    /// The selector budgets and the renderer truncates at this same limit.</summary>
    public int ContextMaxCharactersPerFile { get; set; } = 8_000;

    /// <summary>Per-step timeout. When exceeded, that step is recorded as a failure.</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Maximum acquisition attempts for a single logical step (<c>Evidence:Execution:MaxAttempts</c>,
    /// Milestone 015.3B). <see cref="DefaultMaxAttempts"/> (2) allows ONE bounded retry of a
    /// retry-eligible (Timeout-only) failure; the retry uses the SAME effective provider timeout,
    /// belongs to the same logical step and never bypasses <see cref="MaxConcurrency"/>. Cancellation,
    /// authentication/configuration, schema validation and unexpected errors are never retried.
    /// <c>1</c> disables step-level retry. Clamped to [1, <see cref="MaxAttemptsLimit"/>].
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Default <see cref="MaxAttempts"/>: one initial attempt + one bounded retry.</summary>
    public const int DefaultMaxAttempts = 2;

    /// <summary>Upper clamp for <see cref="MaxAttempts"/> — the step-level retry stays bounded.</summary>
    public const int MaxAttemptsLimit = 5;

    /// <summary>
    /// Maximum acquisition steps executed concurrently (<c>Evidence:Execution:MaxConcurrency</c>,
    /// Milestone 015.1). Bounded parallel execution of INDEPENDENT steps only — every
    /// downstream stage (interpretation, reconciliation, review, packaging, exporters)
    /// stays sequential. <c>1</c> (the default) preserves the historical strictly
    /// sequential execution; results are always aggregated in plan order, never in
    /// completion order.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// What happens when a selected provider cannot execute (<c>Evidence:ProviderFailureMode</c>).
    /// <c>Continue</c> (default) keeps the run going with the remaining providers and records the
    /// failure; <c>FailRun</c> fails the whole run.
    /// </summary>
    public ProviderFailureMode ProviderFailureMode { get; set; } = ProviderFailureMode.Continue;
}
