using System.Text.Json;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reconciliation;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.4A — dedup false-merge hardening. Fixes the single concrete M15.4
/// defect: the false dedup merge `CQL-c55887984d` where bundle RAW-010 ("Duplicated
/// magic string for named HttpClient construction") absorbed the distinct finding
/// RAW-014 ("Inconsistent, uncentralized magic string keys for context items
/// (CustomerID/BrandID casing)") because the titles shared the generic 2-token phrase
/// {magic, string} and RAW-010 carried a SECONDARY observation sharing WriteLog /
/// OnResultExecutionAsync. The hardened identity (M15.4A primary-focus anchor)
/// requires a shared dedup method-part to be a PRIMARY-focus method-part of BOTH
/// findings — a method-part attached to an observation whose normalized title is
/// similar enough to the finding's own title (PrimaryFocusAnchorSimilarity = 0.30,
/// grounded in the persisted M15.2/M15.4 runs: same-defect-rephrased obs ≥ 0.37,
/// distinct-sub-issue obs ≤ 0.25).
/// </summary>
public sealed class DedupFalseMergeHardeningTests
{
    private static readonly RuleBasedFindingReconciler Reconciler = new();

    // ── Builders (mirror DedupReconciliationTests) ───────────────────────────

    private static EngineeringObservation Obs(
        string id, FindingCategory disc, string[]? symbols = null, string file = "src/A.cs",
        int line = 10, string provider = "Claude", string? title = null, string obsType = "GeneralObservation",
        string? extraFile = null, int extraLine = 0)
        => new()
        {
            Id = id,
            Discipline = disc,
            SourceProvider = provider,
            Title = title ?? string.Empty,
            ObservationType = obsType,
            SymbolReferences = symbols ?? [],
            FileReferences = extraFile is null
                ? [new FileReference { Path = file, StartLine = line }]
                : [new FileReference { Path = file, StartLine = line }, new FileReference { Path = extraFile, StartLine = extraLine }],
            LineReferences = extraFile is null ? [line] : [line, extraLine]
        };

    private static Finding Raw(
        string id, FindingCategory disc, string title, string provider,
        FindingSeverity sev = FindingSeverity.Medium, string file = "src/A.cs",
        string[]? obsIds = null)
        => new()
        {
            Id = id,
            Title = title,
            Summary = title,
            Category = disc,
            Severity = sev,
            Confidence = FindingConfidence.Medium,
            EvidenceProvider = provider,
            SupportingProviders = [provider],
            FileReferences = [new FileReference { Path = file, StartLine = 10 }],
            ObservationIds = obsIds ?? [],
            SupportingObservationCount = obsIds?.Length ?? 1
        };

    private static ReconciliationResult Run(IReadOnlyList<Finding> raw, IReadOnlyList<EngineeringObservation> obs)
        => Reconciler.Reconcile(raw, obs);

    // ── The M15.4 false merge (CQL-c55887984d) and its cluster-order safety ──

    [Fact]
    public void M15_4A_persisted_false_merge_pair_stays_separate()
    {
        // Exact persisted M15.4 shape for RAW-010 + RAW-014.
        // RAW-010 (OpenCode, medium, bundle): exact-title primary obs OBS-011 anchors
        //   {configureservices, createclient}; OBS-014 is a SECONDARY obs that happens
        //   to share WriteLog/OnResultExecutionAsync with RAW-014. RAW-014 (ClaudeCode,
        //   low, single obs OBS-117) primary-anchors {writelog, onresultexecutionasync}.
        // The shared method-parts are NOT primary-focus in RAW-010 → no merge.
        var obs = new[]
        {
            Obs("OBS-011", FindingCategory.CodeQuality,
                symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.CreateClient"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Duplicated magic string for named HttpClient construction", obsType: "CodeHotspot"),
            Obs("OBS-014", FindingCategory.CodeQuality,
                symbols: ["RequestEnricher.BuildItemsFromBody", "ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "OpenCode",
                title: "Magic string item keys duplicated across telemetry classes", obsType: "CodeHotspot"),
            Obs("OBS-117", FindingCategory.CodeQuality,
                symbols: ["ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "ClaudeCode",
                title: "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", obsType: "MagicStringInconsistency"),
        };
        var raw = new[]
        {
            Raw("RAW-010", FindingCategory.CodeQuality, "Duplicated magic string for named HttpClient construction", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-011", "OBS-014"]),
            Raw("RAW-014", FindingCategory.CodeQuality, "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", "ClaudeCode",
                sev: FindingSeverity.Low, obsIds: ["OBS-117"]),
        };

        var result = Run(raw, obs);
        Assert.Equal(2, result.ConsolidatedFindings.Count);
        Assert.DoesNotContain(result.ConsolidatedFindings, f => f.IsConsolidated);
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds[0] == "RAW-010");
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds[0] == "RAW-014");
    }

    [Fact]
    public void M15_4A_false_merge_pair_stays_separate_reversed_input()
    {
        var obs = new[]
        {
            Obs("OBS-011", FindingCategory.CodeQuality,
                symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.CreateClient"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Duplicated magic string for named HttpClient construction", obsType: "CodeHotspot"),
            Obs("OBS-014", FindingCategory.CodeQuality,
                symbols: ["ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "OpenCode",
                title: "Magic string item keys duplicated across telemetry classes", obsType: "CodeHotspot"),
            Obs("OBS-117", FindingCategory.CodeQuality,
                symbols: ["ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "ClaudeCode",
                title: "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", obsType: "MagicStringInconsistency"),
        };
        var raw = new[]
        {
            Raw("RAW-010", FindingCategory.CodeQuality, "Duplicated magic string for named HttpClient construction", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-011", "OBS-014"]),
            Raw("RAW-014", FindingCategory.CodeQuality, "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", "ClaudeCode",
                sev: FindingSeverity.Low, obsIds: ["OBS-117"]),
        };

        var forward = Run(raw, obs);
        var reversed = Run(raw.Reverse().ToList(), obs);
        Assert.Equal(forward.ConsolidatedFindings.Select(f => f.Id), reversed.ConsolidatedFindings.Select(f => f.Id));
        Assert.Equal(2, reversed.ConsolidatedFindings.Count);
        Assert.DoesNotContain(reversed.ConsolidatedFindings, f => f.IsConsolidated);
    }

    [Fact]
    public void M15_4A_false_merge_pair_is_cluster_order_safe()
    {
        // Adding an unrelated third finding and reordering must never change the
        // partition: RAW-010 and RAW-014 stay separate under every permutation.
        var obs = new[]
        {
            Obs("OBS-011", FindingCategory.CodeQuality,
                symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.CreateClient"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Duplicated magic string for named HttpClient construction", obsType: "CodeHotspot"),
            Obs("OBS-014", FindingCategory.CodeQuality,
                symbols: ["ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "OpenCode",
                title: "Magic string item keys duplicated across telemetry classes", obsType: "CodeHotspot"),
            Obs("OBS-117", FindingCategory.CodeQuality,
                symbols: ["ContextLogger.WriteLog", "RequestFilter.OnResultExecutionAsync"],
                file: "src/RedirectToService.Telemetry/ContextLogger.cs", line: 42, provider: "ClaudeCode",
                title: "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", obsType: "MagicStringInconsistency"),
            Obs("OBS-105", FindingCategory.CodeQuality,
                symbols: ["ApiUriProvider.BuildAuthorityServerUri"],
                file: "src/RedirectToService.Core/Providers/ApiUrlProvider.cs", line: 124, provider: "Codex",
                title: "ApiUriProvider couples realm and URI derivation with hardcoded literals", obsType: "CodeHotspot"),
        };
        var raw = new[]
        {
            Raw("RAW-010", FindingCategory.CodeQuality, "Duplicated magic string for named HttpClient construction", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-011", "OBS-014"]),
            Raw("RAW-014", FindingCategory.CodeQuality, "Inconsistent, uncentralized magic string keys for context items (CustomerID/BrandID casing)", "ClaudeCode",
                sev: FindingSeverity.Low, obsIds: ["OBS-117"]),
            Raw("RAW-013", FindingCategory.CodeQuality, "ApiUriProvider couples realm and URI derivation with hardcoded literals", "Codex",
                sev: FindingSeverity.Medium, obsIds: ["OBS-105"]),
        };

        var baseline = Partition(Run(raw, obs));
        // A representative sample of permutations.
        var permutations = new[]
        {
            raw.ToList(),
            raw.OrderByDescending(r => r.Id).ToList(),
            [raw[2], raw[0], raw[1]],
            [raw[1], raw[2], raw[0]],
            [raw[0], raw[2], raw[1]],
        };
        foreach (var perm in permutations)
            Assert.Equal(baseline, Partition(Run(perm, obs)));
    }

    private static string Partition(ReconciliationResult result)
        => string.Join("|",
            result.ConsolidatedFindings
                .Select(f => string.Join("+", f.SupportingFindingIds.OrderBy(x => x)))
                .OrderBy(x => x, StringComparer.Ordinal));

    // ── Generality: the 2-token focus guard is no longer the deciding signal ──

    [Fact]
    public void M15_4A_generic_two_token_overlap_is_insufficient_by_itself()
    {
        // Two findings share the generic category phrase {magic, string} AND a shared
        // method-part with close lines — but the shared method-part is only primary-focus
        // in ONE finding (it comes from the other finding's secondary observation). The
        // primary-focus anchor must refuse, regardless of the 2-token focus overlap.
        var obs = new[]
        {
            Obs("O1", FindingCategory.CodeQuality, symbols: ["CreateClient"], file: "src/A.cs", line: 10, provider: "OpenCode",
                title: "Magic string for module A construction duplicated", obsType: "MagicStringDuplication"),
            Obs("O2", FindingCategory.CodeQuality, symbols: ["CreateClient"], file: "src/A.cs", line: 11, provider: "Codex",
                title: "BuildMap derives the realm from a hardcoded string constant", obsType: "HardcodedLiteral"),
            Obs("O3", FindingCategory.CodeQuality, symbols: ["BuildMap"], file: "src/B.cs", line: 30, provider: "Codex",
                title: "Magic string keys used inconsistently across provider classes", obsType: "HardcodedLiteral"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.CodeQuality, "Magic string for module A construction duplicated", "OpenCode", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.CodeQuality, "Magic string keys used inconsistently across provider classes", "Codex", obsIds: ["O2", "O3"]),
        };

        var result = Run(raw, obs);
        Assert.Equal(2, result.ConsolidatedFindings.Count);
        Assert.DoesNotContain(result.ConsolidatedFindings, f => f.IsConsolidated);
    }

    [Fact]
    public void M15_4A_secondary_symbol_inheritance_is_insufficient()
    {
        // A bundle whose PRIMARY focus is the wallet HttpClient carries a SECONDARY
        // readiness-probe observation (CheckHealthAsync). A distinct single-issue
        // finding IS about that readiness probe. Shared CheckHealthAsync + close lines
        // must NOT merge: the bundle's CheckHealthAsync is not primary-focus.
        var obs = new[]
        {
            Obs("O-BUNDLE-HC", FindingCategory.Reliability,
                symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 24, provider: "OpenCode",
                title: "Wallet balance HttpClient registered without timeout, retry, or circuit-breaker policy", obsType: "MissingTimeout"),
            Obs("O-BUNDLE-W", FindingCategory.Reliability,
                symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.GetCustomerBalanceAsync"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Wallet balance HttpClient registered without timeout, retry, or circuit-breaker policy", obsType: "MissingTimeout"),
            Obs("O-SINGLE-READY", FindingCategory.Reliability,
                symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 26, provider: "ClaudeCode",
                title: "Readiness probe checks each distinct realm sequentially", obsType: "SequentialHealthCheckExecution"),
        };
        var raw = new[]
        {
            Raw("RAW-BUNDLE", FindingCategory.Reliability, "Wallet balance HttpClient registered without timeout, retry, or circuit-breaker policy", "OpenCode",
                sev: FindingSeverity.High, obsIds: ["O-BUNDLE-HC", "O-BUNDLE-W"]),
            Raw("RAW-SINGLE", FindingCategory.Reliability, "Readiness probe checks each distinct realm sequentially", "ClaudeCode",
                sev: FindingSeverity.Low, obsIds: ["O-SINGLE-READY"]),
        };

        var result = Run(raw, obs);
        Assert.Equal(2, result.ConsolidatedFindings.Count);
        Assert.DoesNotContain(result.ConsolidatedFindings, f => f.IsConsolidated);
    }

    [Fact]
    public void M15_4A_bundle_still_merges_on_its_primary_issue()
    {
        // The multi-issue bundle is NOT globally forbidden from merging — it merges
        // when the shared method-part is the PRIMARY focus of both members.
        var obs = new[]
        {
            Obs("O-A-NRE", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", obsType: "NullDereference"),
            Obs("O-A-CLOG", FindingCategory.Reliability, symbols: ["WriteLog"], file: "src/ContextLogger.cs", line: 31, provider: "OpenCode",
                title: "ContextLogger silently drops exception details outside http context", obsType: "LoggingAntiPattern"),
            Obs("O-B-NRE", FindingCategory.Reliability, symbols: ["RequestFilter.OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException", obsType: "ExceptionHandling"),
        };
        var raw = new[]
        {
            Raw("RAW-A", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch, breaking the pipeline with NRE", "OpenCode",
                sev: FindingSeverity.High, obsIds: ["O-A-NRE", "O-A-CLOG"]),
            Raw("RAW-B", FindingCategory.Reliability, "Telemetry exception is caught and suppressed but a null telemetry object is then dereferenced, causing an unhandled NullReferenceException", "ClaudeCode",
                sev: FindingSeverity.High, obsIds: ["O-B-NRE"]),
        };

        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
    }

    [Fact]
    public void M15_4A_observation_without_title_contributes_no_identity()
    {
        // Conservative fallback: observations without a title can never be primary-focus,
        // so a title-less observation lends no dedup identity (it cannot create a shared
        // primary method-part). Observed-real pipeline obs always carry titles; this is
        // the safe default for any partial data.
        var obs = new[]
        {
            Obs("O1", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 35, provider: "OpenCode",
                title: null, obsType: "NullDereference"),
            Obs("O2", FindingCategory.Reliability, symbols: ["OnActionExecutionAsync"], file: "src/RequestFilter.cs", line: 37, provider: "ClaudeCode",
                title: null, obsType: "ExceptionHandling"),
        };
        var raw = new[]
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode", obsIds: ["O1"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode", obsIds: ["O2"]),
        };
        Assert.Equal(2, Run(raw, obs).ConsolidatedFindings.Count);
    }

    [Fact]
    public void M15_4A_rephrased_primary_observation_still_anchors()
    {
        // The anchor is NOT an exact-title requirement: a same-defect rephrased
        // observation (persisted M15.2 wallet shape, ~0.37 similarity) is still
        // primary-focus, so the genuine M15.2 wallet merge is preserved.
        var obs = new[]
        {
            Obs("OBS-012", FindingCategory.Reliability, symbols: ["AddHttpClient"], file: "src/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "Outbound wallet HttpClient registered without any timeout or resilience policy", obsType: "MissingTimeout"),
            Obs("OBS-057", FindingCategory.Reliability, symbols: ["ConfigureServices", "HealthCheck", "GetCustomerBalanceAsync"], file: "src/RedirectToServiceCoreModule.cs", line: 29, provider: "OpenCode",
                title: "Wallet HttpClient is registered without an explicit timeout or resilience policy", obsType: "MissingTimeout"),
            Obs("OBS-058", FindingCategory.Reliability, symbols: ["ConfigureServices", "HealthCheck", "GetCustomerBalanceAsync"], file: "src/RedirectToServiceCoreModule.cs", line: 27, provider: "Codex",
                title: "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies", obsType: "MissingRetry"),
        };
        var raw = new[]
        {
            Raw("RAW-011", FindingCategory.Reliability, "Outbound wallet HttpClient registered without any timeout or resilience policy", "OpenCode",
                sev: FindingSeverity.High, obsIds: ["OBS-012", "OBS-057"]),
            Raw("RAW-014", FindingCategory.Reliability, "Wallet API calls use raw HttpClient without visible retry or circuit-breaker policies", "Codex",
                sev: FindingSeverity.Medium, obsIds: ["OBS-058"]),
        };

        var f = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(f.IsConsolidated);
        Assert.Equal("dedup-identity", f.ReconciliationStrategy);
        Assert.Equal("Medium–High", f.SeverityRange);
    }

    // ── The three genuine M15.4 merges must all survive ──────────────────────

    [Fact]
    public void M15_4A_wallet_cluster_merges_all_four_members()
    {
        // REL-42ba184796: RAW-018 + RAW-017 + RAW-020 + RAW-025 (persisted shapes,
        // condensed to the identity-bearing observations). Transitive joins via shared
        // primary-focus method-parts with close lines.
        var obs = new[]
        {
            Obs("OBS-024", FindingCategory.Reliability, symbols: ["AddHttpClient"], file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 29, provider: "OpenCode",
                title: "No explicit timeout configured on downstream HTTP clients (default 100s)", obsType: "MissingTimeout"),
            Obs("OBS-081", FindingCategory.Reliability, symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.HealthCheck", "BalanceContextProvider.GetCustomerBalanceAsync"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "Codex", extraFile: "src/RedirectToService.Core/Providers/BalanceContextProvider.cs", extraLine: 120,
                title: "Named HttpClient instances for wallet calls are registered without an explicit timeout or handler policy", obsType: "MissingTimeout"),
            Obs("OBS-023", FindingCategory.Reliability, symbols: ["GetCustomerBalanceAsync", "AddHttpClient"], file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode", extraFile: "src/RedirectToService.Core/Providers/BalanceContextProvider.cs", extraLine: 120,
                title: "No retry or circuit breaker on downstream wallet HTTP calls", obsType: "CodeHotspot"),
            Obs("OBS-082", FindingCategory.Reliability, symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.GetContextAsync", "BalanceContextProvider.HealthCheck"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "Codex",
                title: "Wallet HTTP clients do not show retry or other transient-fault handling", obsType: "MissingRetry"),
            Obs("OBS-127", FindingCategory.Reliability, symbols: ["BalanceContextProvider.GetCustomerBalanceAsync"], file: "src/RedirectToService.Core/Providers/BalanceContextProvider.cs", line: 120, provider: "ClaudeCode",
                title: "No retry logic for transient wallet API failures leaves redirect decisions degraded on first failure", obsType: "MissingResiliencePattern"),
        };
        var raw = new[]
        {
            Raw("RAW-018", FindingCategory.Reliability, "Wallet balance HttpClient registered without timeout, retry, or circuit-breaker policy", "ClaudeCode",
                sev: FindingSeverity.High, obsIds: ["OBS-024", "OBS-081"]),
            Raw("RAW-017", FindingCategory.Reliability, "No retry or circuit breaker on downstream wallet HTTP calls", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-023"]),
            Raw("RAW-020", FindingCategory.Reliability, "Wallet HTTP clients do not show retry or other transient-fault handling", "Codex",
                sev: FindingSeverity.Medium, obsIds: ["OBS-082"]),
            Raw("RAW-025", FindingCategory.Reliability, "No retry logic for transient wallet API failures leaves redirect decisions degraded on first failure", "ClaudeCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-127"]),
        };

        var result = Run(raw, obs);
        var merged = Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated);
        Assert.Equal("dedup-identity", merged.ReconciliationStrategy);
        Assert.Equal(4, merged.SupportingFindingIds.Count);
        Assert.Contains("RAW-018", merged.SupportingFindingIds);
        Assert.Contains("RAW-017", merged.SupportingFindingIds);
        Assert.Contains("RAW-020", merged.SupportingFindingIds);
        Assert.Contains("RAW-025", merged.SupportingFindingIds);
        Assert.Single(result.ConsolidatedFindings);
    }

    [Fact]
    public void M15_4A_brand_auth_testing_merge_preserved()
    {
        // TST-d4244b718f: RAW-035 + RAW-036, shared primary-focus
        // `userealbrandauthentication` @ IntegrationTestBase.cs:33 on both sides.
        var obs = new[]
        {
            Obs("OBS-095", FindingCategory.Testing,
                symbols: ["IntegrationTestBase.UseRealBrandAuthentication", "TestAuthHandler", "MirrorIssuerIntegrationTests"],
                file: "test/RedirectToService.UnitTests/Integration/IntegrationTestBase.cs", line: 33, provider: "Codex",
                title: "Configured brand-authentication path is not directly exercised by the current integration fixture", obsType: "MissingTests"),
            Obs("OBS-045", FindingCategory.Testing,
                symbols: ["IntegrationTestBase.UseRealBrandAuthentication", "TestAuthHandler.HandleAuthenticateAsync", "TestTokens.Build"],
                file: "test/RedirectToService.UnitTests/Integration/IntegrationTestBase.cs", line: 33, provider: "OpenCode",
                title: "Production brand JWT authentication pipeline is never exercised by tests", obsType: "Testability"),
        };
        var raw = new[]
        {
            Raw("RAW-035", FindingCategory.Testing, "Configured brand-authentication path is not directly exercised by the current integration fixture", "Codex",
                sev: FindingSeverity.Medium, obsIds: ["OBS-095"]),
            Raw("RAW-036", FindingCategory.Testing, "Production brand JWT authentication pipeline is never exercised by tests", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-045"]),
        };

        var merged = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(merged.IsConsolidated);
        Assert.Equal("dedup-identity", merged.ReconciliationStrategy);
        Assert.Contains("RAW-035", merged.SupportingFindingIds);
        Assert.Contains("RAW-036", merged.SupportingFindingIds);
    }

    [Fact]
    public void M15_4A_readiness_probe_merge_preserved()
    {
        // OBS-70131abcc2: RAW-045 + RAW-049, shared primary-focus `checkhealthasync`
        // @ BalanceContextProviderHealthCheck.cs:24 on both sides.
        var obs = new[]
        {
            Obs("OBS-065", FindingCategory.Observability,
                symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 24, provider: "OpenCode",
                title: "Readiness probe iterates every distinct realm sequentially, so /readyz latency grows linearly", obsType: "CodeHotspot"),
            Obs("OBS-104", FindingCategory.Observability,
                symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 24, provider: "Codex",
                title: "Readiness probe checks each distinct realm sequentially", obsType: "SequentialHealthCheckExecution"),
        };
        var raw = new[]
        {
            Raw("RAW-045", FindingCategory.Observability, "Readiness probe iterates every distinct realm sequentially, so /readyz latency grows linearly", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-065"]),
            Raw("RAW-049", FindingCategory.Observability, "Readiness probe checks each distinct realm sequentially", "Codex",
                sev: FindingSeverity.Low, obsIds: ["OBS-104"]),
        };

        var merged = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.True(merged.IsConsolidated);
        Assert.Equal("dedup-identity", merged.ReconciliationStrategy);
        Assert.Contains("RAW-045", merged.SupportingFindingIds);
        Assert.Contains("RAW-049", merged.SupportingFindingIds);
    }

    // ── Evidence / provenance / severity / determinism semantics ─────────────

    [Fact]
    public void M15_4A_merged_finding_preserves_evidence_and_provenance()
    {
        var obs = new[]
        {
            Obs("OBS-065", FindingCategory.Observability, symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 24, provider: "OpenCode",
                title: "Readiness probe iterates every distinct realm sequentially, so /readyz latency grows linearly", obsType: "CodeHotspot"),
            Obs("OBS-104", FindingCategory.Observability, symbols: ["BalanceContextProviderHealthCheck.CheckHealthAsync"],
                file: "src/RedirectToService.Diagnostics/HealthChecks/BalanceContextProviderHealthCheck.cs", line: 24, provider: "Codex",
                title: "Readiness probe checks each distinct realm sequentially", obsType: "SequentialHealthCheckExecution"),
        };
        var raw = new[]
        {
            Raw("RAW-045", FindingCategory.Observability, "Readiness probe iterates every distinct realm sequentially, so /readyz latency grows linearly", "OpenCode", obsIds: ["OBS-065"]),
            Raw("RAW-049", FindingCategory.Observability, "Readiness probe checks each distinct realm sequentially", "Codex", obsIds: ["OBS-104"]),
        };

        var merged = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.Contains("OBS-065", merged.ObservationIds);
        Assert.Contains("OBS-104", merged.ObservationIds);
        Assert.Equal(2, merged.SupportingObservationCount);
        Assert.Equal(["Codex", "OpenCode"], merged.SupportingProviders.OrderBy(x => x));
        Assert.Contains("balancecontextproviderhealthcheck.checkhealthasync", merged.SymbolReferences);
        Assert.Equal(FindingCategory.Observability, merged.Category);
        Assert.Equal(FindingSeverity.Medium, merged.Severity);   // merged severity semantics preserved
    }

    [Fact]
    public void M15_4A_merged_finding_preserves_severity_semantics()
    {
        var obs = new[]
        {
            Obs("OBS-023", FindingCategory.Reliability, symbols: ["GetCustomerBalanceAsync", "AddHttpClient"], file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "OpenCode",
                title: "No retry or circuit breaker on downstream wallet HTTP calls", obsType: "CodeHotspot"),
            Obs("OBS-081", FindingCategory.Reliability, symbols: ["RedirectToServiceCoreModule.ConfigureServices", "BalanceContextProvider.GetCustomerBalanceAsync"],
                file: "src/RedirectToService.Core/RedirectToServiceCoreModule.cs", line: 27, provider: "Codex", extraFile: "src/RedirectToService.Core/Providers/BalanceContextProvider.cs", extraLine: 120,
                title: "Named HttpClient instances for wallet calls are registered without an explicit timeout or handler policy", obsType: "MissingTimeout"),
            Obs("OBS-127", FindingCategory.Reliability, symbols: ["BalanceContextProvider.GetCustomerBalanceAsync"], file: "src/RedirectToService.Core/Providers/BalanceContextProvider.cs", line: 120, provider: "ClaudeCode",
                title: "No retry logic for transient wallet API failures leaves redirect decisions degraded on first failure", obsType: "MissingResiliencePattern"),
        };
        var raw = new[]
        {
            Raw("RAW-018", FindingCategory.Reliability, "Wallet balance HttpClient registered without timeout, retry, or circuit-breaker policy", "ClaudeCode",
                sev: FindingSeverity.High, obsIds: ["OBS-081"]),
            Raw("RAW-017", FindingCategory.Reliability, "No retry or circuit breaker on downstream wallet HTTP calls", "OpenCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-023"]),
            Raw("RAW-025", FindingCategory.Reliability, "No retry logic for transient wallet API failures leaves redirect decisions degraded on first failure", "ClaudeCode",
                sev: FindingSeverity.Medium, obsIds: ["OBS-127"]),
        };

        var merged = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        Assert.Equal("Medium–High", merged.SeverityRange);  // spread recorded, not collapsed
        Assert.False(merged.HasContradiction);              // one-step spread is not a contradiction
        Assert.Equal(FindingSeverity.High, merged.Severity);              // blended severity = max, unchanged
    }

    [Fact]
    public void M15_4A_dedup_ids_are_deterministic_and_not_source_ids()
    {
        var obs = new[]
        {
            Obs("OBS-095", FindingCategory.Testing,
                symbols: ["IntegrationTestBase.UseRealBrandAuthentication"],
                file: "test/RedirectToService.UnitTests/Integration/IntegrationTestBase.cs", line: 33, provider: "Codex",
                title: "Configured brand-authentication path is not directly exercised by the current integration fixture", obsType: "MissingTests"),
            Obs("OBS-045", FindingCategory.Testing,
                symbols: ["IntegrationTestBase.UseRealBrandAuthentication"],
                file: "test/RedirectToService.UnitTests/Integration/IntegrationTestBase.cs", line: 33, provider: "OpenCode",
                title: "Production brand JWT authentication pipeline is never exercised by tests", obsType: "Testability"),
        };
        var raw = new[]
        {
            Raw("RAW-035", FindingCategory.Testing, "Configured brand-authentication path is not directly exercised by the current integration fixture", "Codex", obsIds: ["OBS-095"]),
            Raw("RAW-036", FindingCategory.Testing, "Production brand JWT authentication pipeline is never exercised by tests", "OpenCode", obsIds: ["OBS-045"]),
        };

        var a = Assert.Single(Run(raw, obs).ConsolidatedFindings);
        var b = Assert.Single(Run(raw.Reverse().ToList(), obs).ConsolidatedFindings);
        Assert.Equal(a.Id, b.Id);
        Assert.DoesNotContain(a.Id, new[] { "RAW-035", "RAW-036" });
    }

    // ── Persisted M15.4 replay: the corrected partition ──────────────────────

    private static string M154PersistedDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "outputs", "20260819-195410-1086f6");
    }

    private static (List<Finding> Raw, List<EngineeringObservation> Observations) M154PersistedInputs()
    {
        var dir = M154PersistedDir();
        Assert.True(Directory.Exists(dir), $"Persisted M15.4 run not found at {dir}");
        var raw = JsonSerializer.Deserialize<List<Finding>>(
            File.ReadAllText(Path.Combine(dir, "raw-findings.json")), CouncilJson.Options)!;
        var observations = JsonSerializer.Deserialize<List<EngineeringObservation>>(
            File.ReadAllText(Path.Combine(dir, "observations.json")), CouncilJson.Options)!;
        return (raw, observations);
    }

    [Fact]
    public void M15_4A_persisted_run_replays_corrected_partition()
    {
        // Original M15.4: 55 raw → 49 consolidated (4 dedup merges incl. the false one).
        // M15.4A: 55 → 50 consolidated, exactly THREE genuine dedup merges, and the
        // previously false-merged pair RAW-010/RAW-014 stays separate.
        var (raw, obs) = M154PersistedInputs();
        var result = Reconciler.Reconcile(raw, obs);
        var summary = result.Summary;

        Assert.Equal(55, summary.RawFindingCount);
        Assert.Equal(50, summary.ConsolidatedFindingCount);
        Assert.Equal(55, summary.PreDedupFindingCount);
        Assert.Equal(50, summary.PostDedupFindingCount);
        Assert.Equal(5, summary.DeduplicatedFindingCount);   // 3 genuine merges: 3+1+1 joins
        Assert.Equal(3, result.ConsolidatedFindings.Count(f => f.IsConsolidated));
        Assert.All(result.ConsolidatedFindings.Where(f => f.IsConsolidated),
            f => Assert.Equal("dedup-identity", f.ReconciliationStrategy));

        // The false merge is gone: RAW-010 and RAW-014 are each standalone.
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-010");
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-014");

        // The three genuine merges survived.
        Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated && f.SupportingFindingIds.Contains("RAW-018") && f.SupportingFindingIds.Count == 4);
        Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated && f.SupportingFindingIds.Contains("RAW-035"));
        Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated && f.SupportingFindingIds.Contains("RAW-045"));
    }

    [Fact]
    public void M15_4A_persisted_replay_is_deterministic_across_reversed_input()
    {
        var (raw, obs) = M154PersistedInputs();
        var forward = Reconciler.Reconcile(raw, obs);
        var reversed = Reconciler.Reconcile(raw.Reverse<Finding>().ToList(), obs);

        Assert.Equal(forward.ConsolidatedFindings.Select(f => f.Id), reversed.ConsolidatedFindings.Select(f => f.Id));
        Assert.Equal(forward.Summary.DeduplicatedFindingCount, reversed.Summary.DeduplicatedFindingCount);
        Assert.Equal(Partition(forward), Partition(reversed));
    }

    [Fact]
    public void M15_4A_persisted_replay_keeps_all_near_match_pairs_separate()
    {
        // D-109 near-match pairs from M15.4 must each stay separate under the hardened
        // identity (the anchor only ever makes dedup MORE conservative, so a non-merge
        // under M15.3D cannot become a merge here).
        var (raw, obs) = M154PersistedInputs();
        var result = Reconciler.Reconcile(raw, obs);

        var standalone = result.ConsolidatedFindings
            .Where(f => !f.IsConsolidated)
            .Select(f => f.SupportingFindingIds[0])
            .ToHashSet();

        string[][] nearMatchPairs =
        [
            ["RAW-046", "RAW-051"],   // focus-pass-no-symbol
            ["RAW-050", "RAW-055"],   // config-only same-line false negative
            ["RAW-026", "RAW-043"],   // same secret double-reported cross-discipline
            ["RAW-023", "RAW-034"],   // cross-discipline by design
            ["RAW-002", "RAW-044"],   // cross-discipline by design
        ];
        foreach (var pair in nearMatchPairs)
        {
            Assert.Contains(pair[0], standalone);
            Assert.Contains(pair[1], standalone);
        }

        // RAW-010 and RAW-014, inspected independently after separation: each retains
        // its own discipline, severity and observation set (no cross-contamination).
        var r10 = Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds[0] == "RAW-010");
        var r14 = Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds[0] == "RAW-014");
        Assert.Equal(FindingCategory.CodeQuality, r10.Category);
        Assert.Equal(FindingCategory.CodeQuality, r14.Category);
        Assert.Equal(FindingSeverity.Medium, r10.Severity);
        Assert.Equal(FindingSeverity.Low, r14.Severity);
        Assert.DoesNotContain(r10.ObservationIds, id => r14.ObservationIds.Contains(id));
        Assert.Equal(["Codex", "OpenCode"], r10.SupportingProviders);   // persisted RAW-010 provenance
        Assert.Equal(["ClaudeCode"], r14.SupportingProviders);          // persisted RAW-014 provenance
    }

    [Fact]
    public void M15_4A_package_schemaVersion_stays_1_1_without_new_fields()
    {
        // Package/schema contract: schemaVersion 1.1, no new package fields, existing
        // consumer DTO unchanged. The hardened identity only tightens a deterministic
        // reconciliation predicate — it changes no serializable shape.
        var dir = M154PersistedDir();
        var packageDto = JsonSerializer.Deserialize<EngineeringCouncil.Tests.Contracts.PackageContract>(
            File.ReadAllText(Path.Combine(dir, "engineering-review-package.json")), CouncilJson.Options)!;
        Assert.Equal("1.1", packageDto.SchemaVersion);

        // Reconcile the persisted inputs and confirm the result round-trips through the
        // same finding/observation DTOs (no new serialized fields introduced).
        var (raw, obs) = M154PersistedInputs();
        var result = Reconciler.Reconcile(raw, obs);
        Assert.True(result.ConsolidatedFindings.Count > 0);
        foreach (var f in result.ConsolidatedFindings)
        {
            var roundTripped = JsonSerializer.Deserialize<Finding>(
                JsonSerializer.Serialize(f, CouncilJson.Options), CouncilJson.Options);
            Assert.NotNull(roundTripped);
            Assert.Equal(f.Id, roundTripped!.Id);
        }
    }
}
