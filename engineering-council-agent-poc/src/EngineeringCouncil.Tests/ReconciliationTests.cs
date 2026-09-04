using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Reconciliation;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 010 — deterministic multi-source reconciliation. Verifies the staged
/// grouping rules, provider-independent agreement counting, severity/confidence
/// reconciliation, contradiction flagging, stable ids, and determinism.
/// </summary>
public sealed class ReconciliationTests
{
    private static readonly RuleBasedFindingReconciler Reconciler = new();

    // ── Builders ────────────────────────────────────────────────────────────

    private static EngineeringObservation Obs(
        string id, FindingCategory disc, string type = ObservationTypes.GeneralObservation,
        string? symbol = null, string file = "src/A.cs", int line = 10, string provider = "Claude", string? rule = null)
        => new()
        {
            Id = id,
            Discipline = disc,
            ObservationType = type,
            SourceProvider = provider,
            RuleId = rule,
            SymbolReferences = symbol is null ? [] : [symbol],
            FileReferences = [new FileReference { Path = file, StartLine = line }],
            LineReferences = [line]
        };

    private static Finding Raw(
        string id, FindingCategory disc, string title, string provider,
        FindingSeverity sev = FindingSeverity.Medium, FindingConfidence conf = FindingConfidence.Medium,
        string[]? rules = null, string file = "src/A.cs", int line = 10,
        string[]? obsIds = null, string[]? tags = null)
        => new()
        {
            Id = id,
            Title = title,
            Category = disc,
            Severity = sev,
            Confidence = conf,
            Recommendation = "Fix it.",
            EvidenceProvider = provider,
            SupportingProviders = [provider],
            SourceRules = rules ?? [],
            FileReferences = [new FileReference { Path = file, StartLine = line }],
            ObservationIds = obsIds ?? [],
            SupportingObservationCount = obsIds?.Length ?? 1,
            Tags = tags ?? []
        };

    private static ReconciliationResult Run(IReadOnlyList<Finding> raw, IReadOnlyList<EngineeringObservation> obs)
        => Reconciler.Reconcile(raw, obs);

    // ── 1. Exact rule + location ──────────────────────────────────────────────

    [Fact]
    public void Exact_rule_and_location_reconcile_across_providers()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "SQL injection", "Claude", rules: ["CWE-89"], file: "src/Db.cs", line: 20),
            Raw("RAW-002", FindingCategory.Security, "Injection risk", "SARIF", rules: ["CWE-89"], file: "src/Db.cs", line: 21),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("exact-rule-location", f.ReconciliationStrategy);
        Assert.Equal(2, f.AgreementCount);
    }

    // ── 2. Observation type + symbol ──────────────────────────────────────────

    [Fact]
    public void Same_observation_type_and_symbol_reconcile()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, ObservationTypes.MissingTimeout, symbol: "HttpClient.Send", file: "src/Net.cs", provider: "Claude"),
            Obs("O2", FindingCategory.Reliability, ObservationTypes.MissingTimeout, symbol: "HttpClient.Send", file: "src/Net.cs", provider: "Codex"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "No timeout on outbound call", "Claude", file: "src/Net.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Missing socket deadline", "Codex", file: "src/Net.cs", obsIds: ["O2"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.Equal("type-symbol", f.ReconciliationStrategy);
        Assert.Equal(2, f.AgreementCount);
    }

    // ── 3. Similar title + same file ──────────────────────────────────────────

    [Fact]
    public void Similar_title_and_same_file_reconcile()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "Hard coded secret in config", "Claude", file: "src/Cfg.cs"),
            Raw("RAW-002", FindingCategory.CodeQuality, "Hardcoded credential in config", "Codex", file: "src/Cfg.cs"),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.Equal("title-location", f.ReconciliationStrategy);
    }

    // ── 4. Different disciplines never reconcile ──────────────────────────────

    [Fact]
    public void Different_disciplines_do_not_reconcile()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Same words here", "Claude", rules: ["R1"], file: "src/A.cs"),
            Raw("RAW-002", FindingCategory.Reliability, "Same words here", "Claude", rules: ["R1"], file: "src/A.cs"),
        };
        Assert.Equal(2, Run(raw, []).ConsolidatedFindings.Count);
    }

    // ── 5. Different locations do not reconcile ───────────────────────────────

    [Fact]
    public void Materially_different_locations_do_not_reconcile()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Injection", "Claude", rules: ["CWE-89"], file: "src/A.cs"),
            Raw("RAW-002", FindingCategory.Security, "Injection", "SARIF", rules: ["CWE-89"], file: "src/B.cs"),
        };
        Assert.Equal(2, Run(raw, []).ConsolidatedFindings.Count);
    }

    // ── 6. Single-provider duplicates do not inflate agreement ────────────────

    [Fact]
    public void Single_provider_duplicates_group_without_inflating_agreement()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", rules: ["CWE-798"], file: "src/S.cs", line: 5),
            Raw("RAW-002", FindingCategory.Security, "Secret again", "Claude", rules: ["CWE-798"], file: "src/S.cs", line: 6),
            Raw("RAW-003", FindingCategory.Security, "Another secret", "Claude", rules: ["CWE-798"], file: "src/S.cs", line: 7),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.Equal(1, f.AgreementCount);                      // one provider, not three findings
        Assert.Equal(3, f.SupportingFindingIds.Count);          // but all raw ids preserved
    }

    // ── 7. Multi-provider agreement preserved ─────────────────────────────────

    [Fact]
    public void Multi_provider_agreement_is_preserved()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", rules: ["CWE-798"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "Codex", rules: ["CWE-798"], file: "src/S.cs"),
            Raw("RAW-003", FindingCategory.Security, "Secret", "SARIF", rules: ["CWE-798"], file: "src/S.cs"),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.Equal(3, f.AgreementCount);
        Assert.Equal(["Claude", "Codex", "SARIF"], f.SupportingProviders.OrderBy(x => x));
    }

    // ── 8. Severity range preserved ───────────────────────────────────────────

    [Fact]
    public void Severity_range_is_preserved_and_highest_retained()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", sev: FindingSeverity.High, rules: ["R"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", sev: FindingSeverity.Medium, rules: ["R"], file: "src/S.cs"),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.Equal(FindingSeverity.High, f.Severity);
        Assert.Equal("Medium–High", f.SeverityRange);
    }

    // ── 9. Confidence reconciliation is deterministic ─────────────────────────

    [Fact]
    public void Confidence_is_deterministic_and_boosts_on_deterministic_plus_llm()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", conf: FindingConfidence.Medium, rules: ["R"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", conf: FindingConfidence.Medium, rules: ["R"], file: "src/S.cs"),
        };
        var a = Assert.Single(Run(raw, []).ConsolidatedFindings);
        var b = Assert.Single(Run(raw.Reverse().ToList(), []).ConsolidatedFindings);
        Assert.Equal(FindingConfidence.High, a.Confidence);     // det + llm agreement boosts
        Assert.Equal(a.Confidence, b.Confidence);               // deterministic regardless of order
    }

    [Fact]
    public void Single_provider_confidence_is_not_inflated()
    {
        var raw = new[] { Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", conf: FindingConfidence.Medium, rules: ["R"], file: "src/S.cs") };
        Assert.Equal(FindingConfidence.Medium, Assert.Single(Run(raw, []).ConsolidatedFindings).Confidence);
    }

    // ── 10. Contradictions flagged ────────────────────────────────────────────

    [Fact]
    public void Contradictory_severities_are_flagged_not_silently_merged()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", sev: FindingSeverity.Low, rules: ["R"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", sev: FindingSeverity.Critical, rules: ["R"], file: "src/S.cs"),
        };
        var f = Assert.Single(Run(raw, []).ConsolidatedFindings);
        Assert.True(f.HasContradiction);
        Assert.NotEmpty(f.ContradictionReasons);
    }

    // ── 11. Stable consolidated ids ───────────────────────────────────────────

    [Fact]
    public void Consolidated_ids_are_stable_across_input_order()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", rules: ["CWE-798"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", rules: ["CWE-798"], file: "src/S.cs"),
        };
        var a = Assert.Single(Run(raw, []).ConsolidatedFindings);
        var b = Assert.Single(Run(raw.Reverse().ToList(), []).ConsolidatedFindings);
        Assert.Equal(a.Id, b.Id);
        Assert.StartsWith("SEC-", a.Id);
    }

    // ── 12. Deterministic output order ────────────────────────────────────────

    [Fact]
    public void Reconciliation_output_order_is_deterministic()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", sev: FindingSeverity.High, rules: ["R1"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.CodeQuality, "Complexity", "Codex", sev: FindingSeverity.Low, rules: ["R2"], file: "src/B.cs"),
            Raw("RAW-003", FindingCategory.Reliability, "Timeout", "SARIF", sev: FindingSeverity.Medium, rules: ["R3"], file: "src/C.cs"),
        };
        var order1 = Run(raw, []).ConsolidatedFindings.Select(f => f.Id).ToList();
        var order2 = Run(raw.Reverse().ToList(), []).ConsolidatedFindings.Select(f => f.Id).ToList();
        Assert.Equal(order1, order2);
    }

    // ── 13. Raw findings remain available ─────────────────────────────────────

    [Fact]
    public void Reconciliation_does_not_mutate_the_raw_findings()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", rules: ["R"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", rules: ["R"], file: "src/S.cs"),
        };
        Run(raw, []);
        Assert.Equal(["RAW-001", "RAW-002"], raw.Select(f => f.Id));
        Assert.All(raw, f => Assert.False(f.IsConsolidated));
    }

    // ── 18. Summary metrics ───────────────────────────────────────────────────

    [Fact]
    public void Summary_metrics_are_correct()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Secret", "Claude", rules: ["R"], file: "src/S.cs"),
            Raw("RAW-002", FindingCategory.Security, "Secret", "SARIF", rules: ["R"], file: "src/S.cs"),
            Raw("RAW-003", FindingCategory.Documentation, "Missing docs", "Claude", rules: ["D"], file: "src/D.cs"),
        };
        var result = Run(raw, []);
        Assert.Equal(3, result.Summary.RawFindingCount);
        Assert.Equal(2, result.Summary.ConsolidatedFindingCount);
        Assert.Equal(1, result.Summary.MultiProviderFindingCount);
        Assert.Equal(1, result.Summary.SingleProviderFindingCount);
        Assert.Equal(2, result.Summary.FindingsByProvider["Claude"]);
        Assert.Equal(1, result.Summary.FindingsByProvider["SARIF"]);
    }
}
