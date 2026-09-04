using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Reconciliation;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3D — deterministic finding deduplication. Verifies the
/// dedup-identity stage of the reconciler: suffix-compatible method-symbol matching
/// with close lines and a focus-consistency guard, the negative fixtures A–H
/// (same file different defect, same obs type different symbols, same severity/wording
/// different locations, same endpoint concept different endpoints, same magic-string
/// category different literals, explicit contradiction, shared symbol far apart,
/// cross-discipline), determinism/idempotence, diagnostics counts, and the M15.2
/// regression pairs.
/// </summary>
public sealed class DedupReconciliationTests
{
    private static readonly RuleBasedFindingReconciler Reconciler = new();

    // ── Builders ────────────────────────────────────────────────────────────

    private static EngineeringObservation Obs(
        string id, FindingCategory disc, string[]? symbols = null, string file = "src/A.cs",
        int line = 10, string provider = "Claude", string? title = null)
        => new()
        {
            Id = id,
            Discipline = disc,
            SourceProvider = provider,
            Title = title ?? string.Empty,
            SymbolReferences = symbols ?? [],
            FileReferences = [new FileReference { Path = file, StartLine = line }],
            LineReferences = [line]
        };

    private static Finding Raw(
        string id, FindingCategory disc, string title, string provider,
        FindingSeverity sev = FindingSeverity.Medium, FindingConfidence conf = FindingConfidence.Medium,
        string file = "src/A.cs", int line = 10, string[]? obsIds = null)
        => new()
        {
            Id = id,
            Title = title,
            Summary = title,
            Category = disc,
            Severity = sev,
            Confidence = conf,
            EvidenceProvider = provider,
            SupportingProviders = [provider],
            FileReferences = [new FileReference { Path = file, StartLine = line }],
            ObservationIds = obsIds ?? [],
            SupportingObservationCount = obsIds?.Length ?? 1
        };

    private static ReconciliationResult Run(IReadOnlyList<Finding> raw, IReadOnlyList<EngineeringObservation> obs)
        => Reconciler.Reconcile(raw, obs);

    // ── Positive: dedup-identity stage ────────────────────────────────────────

    [Fact]
    public void Shared_method_symbol_close_lines_and_focus_merge_via_dedup_identity()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
        Assert.Equal(2, f.AgreementCount);
        Assert.Equal(["RAW-001", "RAW-002"], f.SupportingFindingIds.OrderBy(x => x));
    }

    [Fact]
    public void Suffix_compatible_symbols_merge_where_exact_symbol_match_would_not()
    {
        // Fully-qualified vs short method-part: exact sets do not intersect, but the
        // dedup identity sees the shared method-part `onactionexecutionasync`.
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 36, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
    }

    [Fact]
    public void Dedup_merged_id_is_deterministic_and_not_first_arrival_id()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["Wallet.GetCustomerBalanceAsync"], file: "src/Wallet.cs", line: 75, provider: "OpenCode",
                title: "Outbound wallet HttpClient registered without any timeout or resilience policy"),
            Obs("O2", FindingCategory.Reliability, symbols: ["GetCustomerBalanceAsync"], file: "src/Wallet.cs", line: 78, provider: "Codex",
                title: "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "Outbound wallet HttpClient registered without any timeout or resilience policy", "OpenCode", file: "src/Wallet.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies", "Codex", file: "src/Wallet.cs", obsIds: ["O2"]),
        };
        var a = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        var b = Assert.Single(Run(raw.Reverse().ToList(), obs).ConsolidatedFindings);
        Assert.Equal(a.Id, b.Id);                     // order independent
        string[] rawIds = ["RAW-001", "RAW-002"];
        Assert.DoesNotContain(a.Id, rawIds);          // deterministic, not first-arrival
        Assert.StartsWith("REL-", a.Id);
    }

    [Fact]
    public void Dedup_reconciliation_is_idempotent_and_order_independent()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
        };
        var once = Run(raw, obs);
        var twice = Run(raw, obs);
        Assert.Equal(once.ConsolidatedFindings.Select(f => f.Id), twice.ConsolidatedFindings.Select(f => f.Id));
        var reversed = Run(raw.Reverse().ToList(), obs);
        Assert.Equal(once.ConsolidatedFindings.Select(f => f.Id), reversed.ConsolidatedFindings.Select(f => f.Id));
    }

    [Fact]
    public void Dedup_diagnostics_counts_are_correct()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
            Obs("O3", FindingCategory.CodeQuality, symbols: ["BuildAuthorityServerUri"], file: "src/ApiUrlProvider.cs", line: 64, provider: "OpenCode",
                title: "ApiUrlProvider couples realm derivation with hardcoded environment literals"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
            Raw("RAW-003", FindingCategory.CodeQuality, "ApiUrlProvider couples realm derivation with hardcoded environment literals", "OpenCode", file: "src/ApiUrlProvider.cs", obsIds: ["O3"]),
        };
        var s = Run(raw, obs).Summary;
        Assert.Equal(3, s.RawFindingCount);
        Assert.Equal(2, s.ConsolidatedFindingCount);
        Assert.Equal(2, s.PostDedupFindingCount);     // post-dedup == consolidated
        Assert.Equal(3, s.PreDedupFindingCount);      // one dedup join removed one finding
        Assert.Equal(1, s.DeduplicatedFindingCount);  // PreDedup - PostDedup
    }

    [Fact]
    public void Dedup_merge_preserves_all_evidence_from_both_members()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.Contains("O1", f.ObservationIds);
        Assert.Contains("O2", f.ObservationIds);
        Assert.Contains("onactionexecutionasync", f.SymbolReferences);
        Assert.Equal(2, f.SupportingObservationCount);
        Assert.Equal(["ClaudeCode", "OpenCode"], f.SupportingProviders.OrderBy(x => x));
        Assert.Equal("Medium", f.SeverityRange);
    }

    // ── Negative fixtures A–H ─────────────────────────────────────────────────

    [Fact]
    public void A_Same_file_same_symbol_different_defect_do_not_merge()
    {
        // Same file and close lines on the SAME method-part, but the full symbols are
        // suffix-compatible (ContextLogger.WriteLog vs WriteLog) so the type-symbol
        // stage is out of reach. The dedup focus guard must refuse: the two titles
        // share no significant token (RequestFilter-NRE defect vs ContextLogger defect).
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["ContextLogger.WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 32, provider: "ClaudeCode",
                title: "ContextLogger silently drops exception details outside http context"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", file: "src/ContextLogger.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "ContextLogger silently drops exception details outside http context", "ClaudeCode", file: "src/ContextLogger.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void B_Same_obs_type_different_symbols_do_not_merge()
    {
        // No shared method-part, different files, different focus → no stage can fire.
        var obs = new[]
        {
            Obs("O1", FindingCategory.CodeQuality, symbols: ["CreateClient"], file: "src/Provider.cs", line: 80, provider: "OpenCode",
                title: "CreateClient hardcodes environment literals"),
            Obs("O2", FindingCategory.CodeQuality, symbols: ["BuildMap"], file: "src/Mapper.cs", line: 90, provider: "Codex",
                title: "BuildMap derives realm from a hardcoded constant"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "CreateClient hardcodes environment literals", "OpenCode", file: "src/Provider.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.CodeQuality, "BuildMap derives realm from a hardcoded constant", "Codex", file: "src/Mapper.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void C_Same_severity_and_wording_different_locations_do_not_merge()
    {
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "Null-forgiving dereference can throw", "OpenCode", sev: FindingSeverity.Medium, file: "src/A.cs", line: 10),
            Raw("RAW-002", FindingCategory.CodeQuality, "Null-forgiving dereference can throw", "Codex", sev: FindingSeverity.Medium, file: "src/B.cs", line: 200),
        };
        Assert.Equal(2, Run(raw, []).ConsolidatedFindings.Count);
    }

    [Fact]
    public void D_Same_endpoint_concept_different_endpoints_do_not_merge()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Observability, symbols: ["RedirectsController.Health"], file: "src/RedirectsController.cs", line: 48, provider: "OpenCode",
                title: "api redirect healthy always returns ok without real validation"),
            Obs("O2", FindingCategory.Observability, symbols: ["ValidateHealthCheckStatus"], file: "src/Program.cs", line: 5, provider: "OpenCode",
                title: "CI health validation path and buildNumber probe diverge from app configuration"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Observability, "api redirect healthy always returns ok without real validation", "OpenCode", file: "src/RedirectsController.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Observability, "CI health validation path and buildNumber probe diverge from app configuration", "OpenCode", file: "src/Program.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void E_Same_magic_string_category_different_literals_do_not_merge()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.CodeQuality, symbols: ["CreateClient"], file: "src/BalanceContextProvider.cs", line: 106, provider: "OpenCode",
                title: "HTTP client name string duplicated between module and provider"),
            Obs("O2", FindingCategory.CodeQuality, symbols: ["GetCustomerBalanceAsync"], file: "src/BalanceContextProvider.cs", line: 80, provider: "OpenCode",
                title: "Endpoint paths and header names hardcoded in provider"),
            Obs("O3", FindingCategory.CodeQuality, symbols: ["BuildAuthorityServerUri"], file: "src/ApiUrlProvider.cs", line: 64, provider: "OpenCode",
                title: "ApiUrlProvider couples realm and URI derivation with hardcoded literals"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "HTTP client name string duplicated between module and provider", "OpenCode", file: "src/BalanceContextProvider.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.CodeQuality, "Endpoint paths and header names hardcoded in provider", "OpenCode", file: "src/BalanceContextProvider.cs", obsIds: ["O2"]),
            Raw("RAW-003", FindingCategory.CodeQuality, "ApiUrlProvider couples realm and URI derivation with hardcoded literals", "OpenCode", file: "src/ApiUrlProvider.cs", obsIds: ["O3"]),
        };
        Assert.Equal(3, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void F_Explicit_contradiction_is_never_deduplicated_away()
    {
        // Suffix-compatible symbols (HealthController.Health vs Health) keep the
        // type-symbol stage out of reach; the titles SHARE focus tokens (health,
        // endpoint) so the dedup identity would otherwise accept the pair — only the
        // material severity contradiction (Low vs Critical) must keep them separate.
        var obs = new[]
        {
            Obs("O1", FindingCategory.Security, symbols: ["HealthController.Health"], file: "src/Health.cs", line: 50, provider: "OpenCode",
                title: "Health endpoint is insecure"),
            Obs("O2", FindingCategory.Security, symbols: ["Health"], file: "src/Health.cs", line: 51, provider: "Codex",
                title: "Health endpoint authentication is dangerously weak"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Health endpoint is insecure", "OpenCode", sev: FindingSeverity.Low, file: "src/Health.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Security, "Health endpoint authentication is dangerously weak", "Codex", sev: FindingSeverity.Critical, file: "src/Health.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void G_Shared_symbol_without_close_lines_or_same_file_do_not_merge()
    {
        // Both cases pass the focus guard (shared {wallet, timeout}) and share the
        // suffix-compatible method-part {sendasync}, but fail the close-line / same-file
        // requirement of the dedup identity — and the titles are far enough apart that
        // the title-location stage cannot rescue the merge.
        var obsFar = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["Net.SendAsync"], file: "src/Net.cs", line: 10, provider: "OpenCode",
                title: "Wallet configuration registers HttpClient without timeout or resilience policies on the outbound channel"),
            Obs("O2", FindingCategory.Reliability, symbols: ["SendAsync"], file: "src/Net.cs", line: 500, provider: "ClaudeCode",
                title: "Wallet service makes downstream remote calls with no timeout and uses blocking IO repeatedly during request handling"),
        };
        var rawFar = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "Wallet configuration registers HttpClient without timeout or resilience policies on the outbound channel", "OpenCode", file: "src/Net.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Wallet service makes downstream remote calls with no timeout and uses blocking IO repeatedly during request handling", "ClaudeCode", file: "src/Net.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(rawFar, obsFar).ConsolidatedFindings.Count);

        var obsDiffFile = new[]
        {
            Obs("O3", FindingCategory.Reliability, symbols: ["Net.SendAsync"], file: "src/Net.cs", line: 10, provider: "OpenCode",
                title: "Wallet configuration registers HttpClient without timeout or resilience policies on the outbound channel"),
            Obs("O4", FindingCategory.Reliability, symbols: ["SendAsync"], file: "src/OtherNet.cs", line: 10, provider: "ClaudeCode",
                title: "Wallet service makes downstream remote calls with no timeout and uses blocking IO repeatedly during request handling"),
        };
        var rawDiffFile = new[]
        {
            Raw("RAW-003", FindingCategory.Reliability, "Wallet configuration registers HttpClient without timeout or resilience policies on the outbound channel", "OpenCode", file: "src/Net.cs", obsIds: ["O3"]),
            Raw("RAW-004", FindingCategory.Reliability, "Wallet service makes downstream remote calls with no timeout and uses blocking IO repeatedly during request handling", "ClaudeCode", file: "src/OtherNet.cs", obsIds: ["O4"]),
        };
        Assert.Equal(2, Run(rawDiffFile, obsDiffFile).ConsolidatedFindings.Count);
    }

    [Fact]
    public void H_Cross_discipline_never_merge_even_with_shared_symbol_and_lines()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Security, symbols: ["RedirectsController.Health"], file: "src/RedirectsController.cs", line: 48, provider: "Codex",
                title: "Health controller exposes an unauthenticated POST endpoint"),
            Obs("O2", FindingCategory.Observability, symbols: ["RedirectsController.Health"], file: "src/RedirectsController.cs", line: 50, provider: "OpenCode",
                title: "api redirect healthy always returns ok without real validation"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Security, "Health controller exposes an unauthenticated POST endpoint", "Codex", file: "src/RedirectsController.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Observability, "api redirect healthy always returns ok without real validation", "OpenCode", file: "src/RedirectsController.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    // ── Bundle / focus-consistency guards (the M15.3D false-merge trap) ───────

    [Fact]
    public void Bundle_does_not_absorb_a_distinct_single_issue_finding()
    {
        // RAW-012-style bundle: title focuses on the RequestFilter NRE, but the bundle
        // ALSO carries a ContextLogger observation. RAW-017-style single finding is
        // about ContextLogger alone. Shared `writelog` + close lines must NOT merge:
        // the bundle's title focus is RequestFilter, not ContextLogger.
        var obs = new[]
        {
            Obs("O-BUNDLE-NRE", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE"),
            Obs("O-BUNDLE-CLOG", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger silently drops exception details outside http context"),
            Obs("O-SINGLE-CLOG", FindingCategory.Reliability, symbols: ["ContextLogger.WriteLog"], file: "src/ContextLogger.cs", line: 32, provider: "ClaudeCode",
                title: "ContextLogger silently drops exception details when invoked outside an HTTP request context"),
        };
        var raw = new[]
        {
            Raw("RAW-BUNDLE", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", "OpenCode",
                file: "src/RequestFilter.cs", obsIds: ["O-BUNDLE-NRE", "O-BUNDLE-CLOG"]),
            Raw("RAW-SINGLE", FindingCategory.Reliability, "ContextLogger silently drops exception details when invoked outside an HTTP request context", "ClaudeCode",
                file: "src/ContextLogger.cs", obsIds: ["O-SINGLE-CLOG"]),
        };
        var result = Run(raw, obs);
        Assert.Equal(2, result.ConsolidatedFindings.Count);
    }

    [Fact]
    public void Bundle_merges_with_finding_sharing_its_primary_issue()
    {
        // RAW-012 + RAW-016 style: the bundle's title-focus (RequestFilter NRE) matches
        // the other finding's focus, so the merge is safe even though both are bundles.
        var obs = new[]
        {
            Obs("O-A-NRE", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE"),
            Obs("O-A-CLOG", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger silently drops exception details outside http context"),
            Obs("O-B-NRE", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException"),
            Obs("O-B-CATCH", FindingCategory.Reliability, symbols: ["RedirectAppService.RedirectToAsync"], file: "src/RedirectAppService.cs", line: 60, provider: "ClaudeCode",
                title: "RedirectToAsync suppresses the exception and returns the stale result"),
        };
        var raw = new[]
        {
            Raw("RAW-A", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", "OpenCode",
                file: "src/RequestFilter.cs", obsIds: ["O-A-NRE", "O-A-CLOG"]),
            Raw("RAW-B", FindingCategory.Reliability, "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException", "ClaudeCode",
                file: "src/RequestFilter.cs", obsIds: ["O-B-NRE", "O-B-CATCH"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
    }

    [Fact]
    public void Shared_symbol_with_unrelated_titles_does_not_merge()
    {
        // Wallet-resilience vs health-check-latency style: shared `checkhealthasync`
        // symbol with close lines, but the titles share no significant tokens and
        // neither names the symbol → different defects, no merge.
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"], file: "src/HealthCheck.cs", line: 24, provider: "OpenCode",
                title: "Outbound wallet HttpClient registered without any timeout or resilience policy"),
            Obs("O2", FindingCategory.Reliability, symbols: ["CheckHealthAsync"], file: "src/HealthCheck.cs", line: 26, provider: "ClaudeCode",
                title: "Health check probes downstream realms serially with no per-call timeout allowing one slow dependency to stall the whole readiness probe"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "Outbound wallet HttpClient registered without any timeout or resilience policy", "OpenCode", file: "src/HealthCheck.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Health check probes downstream realms serially with no per-call timeout allowing one slow dependency to stall the whole readiness probe", "ClaudeCode", file: "src/HealthCheck.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void Same_file_alone_never_merges()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Observability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger discards the caller message outside an http request"),
            Obs("O2", FindingCategory.Observability, symbols: ["GetCustomerID"], file: "src/ContextLogger.cs", line: 58, provider: "OpenCode",
                title: "GetCustomerID silently swallows exceptions during enrichment"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Observability, "ContextLogger discards the caller message outside an http request", "OpenCode", file: "src/ContextLogger.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Observability, "GetCustomerID silently swallows exceptions during enrichment", "OpenCode", file: "src/ContextLogger.cs", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    // ── M15.2 regression pairs (real persisted evidence, condensed) ───────────

    [Fact]
    public void M15_2_RequestFilter_NRE_pair_merges()
    {
        var obs = new[]
        {
            Obs("OBS-014", FindingCategory.Reliability, symbols: ["GetCustomerBalanceAsync"], file: "src/BalanceContextProvider.cs", line: 120, provider: "OpenCode",
                title: "Wallet HttpClient registered without an explicit timeout or retry policy"),
            Obs("OBS-015", FindingCategory.Reliability, symbols: ["RedirectToAsync", "GetContextAsync"], file: "src/RedirectAppService.cs", line: 60, provider: "OpenCode",
                title: "RedirectToAsync suppresses the exception and returns the stale result"),
            Obs("OBS-016", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync", "GetTelemetryBodyRequest"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE"),
            Obs("OBS-017", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger silently drops exception details outside http context"),
            Obs("OBS-060", FindingCategory.Reliability, symbols: ["CheckHealthAsync", "HealthCheck"], file: "src/BalanceContextProviderHealthCheck.cs", line: 26, provider: "Codex",
                title: "BalanceContextProvider readiness health check runs without a per-call timeout"),
            Obs("OBS-071", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "ClaudeCode",
                title: "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException"),
            Obs("OBS-074", FindingCategory.Reliability, symbols: ["GetContextAsync", "RedirectToAsync"], file: "src/BalanceContextProvider.cs", line: 46, provider: "ClaudeCode",
                title: "GetContextAsync returns the redirect context before the auth handler has completed"),
        };
        var raw = new[]
        {
            Raw("RAW-012", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", "OpenCode",
                sev: FindingSeverity.High, file: "src/RequestFilter.cs", obsIds: ["OBS-014", "OBS-015", "OBS-016", "OBS-017", "OBS-060"]),
            Raw("RAW-016", FindingCategory.Reliability, "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException", "ClaudeCode",
                sev: FindingSeverity.High, file: "src/RequestFilter.cs", obsIds: ["OBS-071", "OBS-074"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
    }

    [Fact]
    public void M15_2_Wallet_httpclient_pair_merges()
    {
        var obs = new[]
        {
            Obs("OBS-012", FindingCategory.Reliability, symbols: ["AddHttpClient"], file: "src/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Outbound wallet HttpClient registered without any timeout or resilience policy"),
            Obs("OBS-013", FindingCategory.Reliability, symbols: ["CheckHealthAsync", "HealthCheck"], file: "src/BalanceContextProviderHealthCheck.cs", line: 24, provider: "OpenCode",
                title: "BalanceContextProvider readiness health check runs without a per-call timeout"),
            Obs("OBS-057", FindingCategory.Reliability, symbols: ["ConfigureServices", "HealthCheck", "GetCustomerBalanceAsync"], file: "src/RedirectToServiceCoreModule.cs", line: 29, provider: "OpenCode",
                title: "Wallet HttpClient is registered without an explicit timeout or resilience policy"),
            Obs("OBS-070", FindingCategory.Reliability, symbols: ["ConfigureServices", "CreateClient", "GetCustomerBalanceAsync"], file: "src/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Outbound wallet HttpClient registered without any retry or circuit-breaker policy"),
            Obs("OBS-058", FindingCategory.Reliability, symbols: ["ConfigureServices", "HealthCheck", "GetCustomerBalanceAsync"], file: "src/RedirectToServiceCoreModule.cs", line: 27, provider: "Codex",
                title: "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies"),
        };
        var raw = new[]
        {
            Raw("RAW-011", FindingCategory.Reliability, "Outbound wallet HttpClient registered without any timeout or resilience policy", "OpenCode",
                sev: FindingSeverity.High, file: "src/RedirectToServiceCoreModule.cs", obsIds: ["OBS-012", "OBS-013", "OBS-057", "OBS-070"]),
            Raw("RAW-014", FindingCategory.Reliability, "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies", "Codex",
                sev: FindingSeverity.Medium, file: "src/RedirectToServiceCoreModule.cs", obsIds: ["OBS-058"]),
        };
        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
        Assert.Equal("Medium–High", f.SeverityRange);   // severity spread recorded, not erased
        Assert.False(f.HasContradiction);               // one-step spread is not a contradiction
    }

    [Fact]
    public void M15_2_Health_endpoint_pair_stays_separate()
    {
        var obs = new[]
        {
            Obs("OBS-045", FindingCategory.Observability, symbols: ["RedirectsController.Health"], file: "src/RedirectsController.cs", line: 48, provider: "OpenCode",
                title: "api redirect healthy always returns ok without real validation"),
            Obs("OBS-046", FindingCategory.Observability, symbols: ["PreConfigureServices"], file: "src/RedirectToServiceDiagnosticsModule.cs", line: 17, provider: "OpenCode",
                title: "PreConfigureServices registers the diagnostics routes unconditionally"),
            Obs("OBS-047", FindingCategory.Observability, symbols: ["ValidateHealthCheckStatus"], file: "src/Program.cs", line: 5, provider: "OpenCode",
                title: "CI health validation path and buildNumber probe diverge from app configuration"),
            Obs("OBS-051", FindingCategory.Observability, symbols: ["Program"], file: "src/Program.cs", line: 5, provider: "OpenCode",
                title: "Program wires the diagnostics module before the health endpoint is registered"),
        };
        var raw = new[]
        {
            Raw("RAW-028", FindingCategory.Observability, "api redirect healthy always returns ok without real validation", "OpenCode",
                file: "src/RedirectsController.cs", obsIds: ["OBS-045", "OBS-046"]),
            Raw("RAW-029", FindingCategory.Observability, "CI health validation path and buildNumber probe diverge from app configuration", "OpenCode",
                file: "src/Program.cs", obsIds: ["OBS-047", "OBS-051"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void M15_2_Magic_string_trio_stays_separate()
    {
        var obs = new[]
        {
            Obs("OBS-001", FindingCategory.CodeQuality, symbols: ["CreateClient"], file: "src/BalanceContextProvider.cs", line: 106, provider: "OpenCode",
                title: "HTTP client name string duplicated between module registration and provider"),
            Obs("OBS-002", FindingCategory.CodeQuality, symbols: ["AddHttpClient"], file: "src/RedirectToServiceCoreModule.cs", line: 29, provider: "OpenCode",
                title: "AddHttpClient registers a client by a name string reused elsewhere"),
            Obs("OBS-006", FindingCategory.CodeQuality, symbols: ["GetCustomerBalanceAsync"], file: "src/BalanceContextProvider.cs", line: 80, provider: "OpenCode",
                title: "Endpoint paths and header names hardcoded in BalanceContextProvider"),
            Obs("OBS-004", FindingCategory.CodeQuality, symbols: ["BuildAuthorityServerUri"], file: "src/ApiUrlProvider.cs", line: 64, provider: "OpenCode",
                title: "ApiUrlProvider couples realm/URI/issuer derivation with hardcoded environment literals"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "HTTP client name string duplicated between module registration and provider", "OpenCode",
                file: "src/BalanceContextProvider.cs", obsIds: ["OBS-001", "OBS-002"]),
            Raw("RAW-005", FindingCategory.CodeQuality, "Endpoint paths and header names hardcoded in BalanceContextProvider", "OpenCode",
                file: "src/BalanceContextProvider.cs", obsIds: ["OBS-006"]),
            Raw("RAW-003", FindingCategory.CodeQuality, "ApiUrlProvider couples realm/URI/issuer derivation with hardcoded environment literals", "OpenCode",
                file: "src/ApiUrlProvider.cs", obsIds: ["OBS-004"]),
        };
        Assert.Equal(3, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void M15_2_ContextLogger_finding_survives_the_requestfilter_merge()
    {
        // RAW-012 + RAW-016 merge; RAW-017 (ContextLogger) must remain separate even
        // though RAW-012 carries a ContextLogger observation as a secondary issue.
        var obs = new[]
        {
            Obs("OBS-016", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE"),
            Obs("OBS-017", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger silently drops exception details outside http context"),
            Obs("OBS-071", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "ClaudeCode",
                title: "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException"),
            Obs("OBS-072", FindingCategory.Reliability, symbols: ["ContextLogger.WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "ClaudeCode",
                title: "ContextLogger silently drops exception details when invoked outside an HTTP request context"),
        };
        var raw = new[]
        {
            Raw("RAW-012", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", "OpenCode",
                sev: FindingSeverity.High, file: "src/RequestFilter.cs", obsIds: ["OBS-016", "OBS-017"]),
            Raw("RAW-016", FindingCategory.Reliability, "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException", "ClaudeCode",
                sev: FindingSeverity.High, file: "src/RequestFilter.cs", obsIds: ["OBS-071"]),
            Raw("RAW-017", FindingCategory.Reliability, "ContextLogger silently drops exception details when invoked outside an HTTP request context", "ClaudeCode",
                sev: FindingSeverity.Medium, file: "src/ContextLogger.cs", obsIds: ["OBS-072"]),
        };
        var result = Run(raw, obs);
        Assert.Equal(2, result.ConsolidatedFindings.Count);

        var merged = Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated);
        Assert.Contains("RAW-012", merged.SupportingFindingIds);
        Assert.Contains("RAW-016", merged.SupportingFindingIds);
        Assert.DoesNotContain("RAW-017", merged.SupportingFindingIds);

        var standalone = Assert.Single(result.ConsolidatedFindings, f => !f.IsConsolidated);
        Assert.Equal(["RAW-017"], standalone.MergedFromFindingIds);
    }

    [Fact]
    public void Deterministic_output_order_with_mixed_dedup_and_standalone()
    {
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch"),
            Obs("O2", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Null telemetry object dereferenced causing NullReferenceException"),
            Obs("O3", FindingCategory.CodeQuality, symbols: ["BuildMap"], file: "src/ApiUrlProvider.cs", line: 90, provider: "OpenCode",
                title: "ApiUrlProvider couples realm derivation with hardcoded literals"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", sev: FindingSeverity.High, file: "src/RequestFilter.cs", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", file: "src/RequestFilter.cs", obsIds: ["O2"]),
            Raw("RAW-003", FindingCategory.CodeQuality, "ApiUrlProvider couples realm derivation with hardcoded literals", "OpenCode", sev: FindingSeverity.Low, file: "src/ApiUrlProvider.cs", obsIds: ["O3"]),
        };
        var order1 = Run(raw, obs).ConsolidatedFindings.Select(f => f.Id).ToList();
        var order2 = Run(raw.Reverse().ToList(), obs).ConsolidatedFindings.Select(f => f.Id).ToList();
        Assert.Equal(order1, order2);
    }
}