using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Sarif;

/// <summary>
/// The complete, deterministic SARIF → engineering mapping rules (Milestone 009).
/// Every mapping is documented here and in ADR-010. Nothing is guessed: an
/// unrecognized signal maps to <see cref="FindingCategory.Unknown"/> /
/// <see cref="ObservationTypes.Unknown"/>, never an invented meaning.
/// </summary>
internal static class SarifMappings
{
    // ── Severity ──────────────────────────────────────────────────────────────
    // SARIF level → engineering severity. Complete table:
    //   error → Critical · warning → High · note → Medium · none → Low
    // A missing/unrecognized level is treated as "warning" (the SARIF default) and
    // is flagged as a metadata deficiency by the confidence rules.
    public static FindingSeverity Severity(string? level) => (level?.Trim().ToLowerInvariant()) switch
    {
        "error" => FindingSeverity.Critical,
        "warning" => FindingSeverity.High,
        "note" => FindingSeverity.Medium,
        "none" => FindingSeverity.Low,
        _ => FindingSeverity.High
    };

    public static bool IsKnownLevel(string? level) => (level?.Trim().ToLowerInvariant()) is
        "error" or "warning" or "note" or "none";

    // ── Discipline ────────────────────────────────────────────────────────────
    // Deterministic keyword scan over the combined signal (rule id + name + tags +
    // descriptions + properties). First matching group wins, in this order.
    private static readonly (FindingCategory Discipline, string[] Signals)[] DisciplineRules =
    [
        (FindingCategory.Security,
            ["cwe", "security", "auth", "authentication", "authorization", "inject", "sql", "xss",
             "secret", "password", "credential", "crypto", "cipher", "ssrf", "csrf", "vuln", "sanitiz"]),
        (FindingCategory.Reliability,
            ["null", "nullab", "dispos", "concurren", "synchron", "thread", "race", "deadlock",
             "resilien", "retry", "timeout", "leak", "await", "cancellation"]),
        (FindingCategory.Testing,
            ["test", "assert", "mock", "xunit", "nunit", "coverage", "fixture"]),
        (FindingCategory.Observability,
            ["log", "logging", "trace", "tracing", "metric", "telemetry", "health", "diagnostic"]),
        (FindingCategory.Documentation,
            ["doc", "documentation", "comment", "xmldoc", "summary-tag"]),
        (FindingCategory.Architecture,
            ["depend", "layer", "coupl", "cohesion", "cycle", "circular", "architecture", "namespace"]),
        (FindingCategory.CodeQuality,
            ["complex", "maintainab", "style", "format", "duplicat", "naming", "readab", "smell",
             "unused", "dead-code", "simplif"]),
    ];

    public static FindingCategory Discipline(string signal)
    {
        foreach (var (discipline, signals) in DisciplineRules)
            if (signals.Any(s => signal.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return discipline;
        return FindingCategory.Unknown;
    }

    // ── Observation type ──────────────────────────────────────────────────────
    // Deterministic keyword scan → a known observation type, else Unknown.
    private static readonly (string Type, string[] Signals)[] TypeRules =
    [
        (ObservationTypes.HardcodedSecret, ["secret", "password", "credential", "hardcoded", "api-key", "apikey"]),
        (ObservationTypes.MissingAuthorization, ["authorization", "authoriz", "access-control"]),
        (ObservationTypes.HighComplexity, ["complex", "cyclomatic", "cognitive"]),
        (ObservationTypes.MissingTimeout, ["timeout"]),
        (ObservationTypes.MissingRetryPolicy, ["retry", "resilien"]),
        (ObservationTypes.BroadExceptionHandling, ["exception", "catch", "swallow"]),
        (ObservationTypes.CircularDependency, ["circular", "cycle"]),
        (ObservationTypes.LayerViolation, ["layer", "coupl"]),
        (ObservationTypes.MissingDocumentation, ["documentation", "xmldoc", "missing-doc"]),
        (ObservationTypes.MissingTests, ["missing-test", "untested", "no-test"]),
        (ObservationTypes.MissingHealthCheck, ["health"]),
        (ObservationTypes.CodeHotspot, ["hotspot", "duplicat"]),
    ];

    public static string ObservationType(string signal)
    {
        foreach (var (type, signals) in TypeRules)
            if (signals.Any(s => signal.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return type;
        return ObservationTypes.Unknown;
    }

    // ── Confidence ────────────────────────────────────────────────────────────
    // Static analyzers are deterministic: default 0.95 (High). Confidence is only
    // reduced for concrete data deficiencies (never invented heuristics):
    //   0 deficiencies → High (0.95) · 1 → Medium (0.6) · 2+ → Low (0.3)
    public static (FindingConfidence Level, double Score) Confidence(int deficiencies) => deficiencies switch
    {
        <= 0 => (FindingConfidence.High, 0.95),
        1 => (FindingConfidence.Medium, 0.60),
        _ => (FindingConfidence.Low, 0.30)
    };
}
