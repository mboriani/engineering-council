using System.Text.Json;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reconciliation;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3D — replays the persisted M15.2 run
/// (<c>outputs/20260813-230240-b3e36e</c>) through the UPDATED deterministic
/// reconciliation path and asserts the conservative dedup outcome: 29 raw findings
/// → 27 consolidated, exactly two dedup-identity merges (RequestFilter NRE pair
/// RAW-012+RAW-016; wallet HttpClient pair RAW-011+RAW-014), and every near-match
/// the milestone must NOT merge staying separate (health endpoint pair, magic-string
/// trio, ContextLogger finding, cross-discipline RequestFilter views).
/// </summary>
public sealed class PersistedRunDedupReplayTests
{
    private static readonly RuleBasedFindingReconciler Reconciler = new();

    private static string PersistedDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "outputs", "20260813-230240-b3e36e");
    }

    private static ReconciliationResult Replay()
    {
        var dir = PersistedDir();
        Assert.True(Directory.Exists(dir), $"Persisted M15.2 run not found at {dir}");

        var raw = JsonSerializer.Deserialize<List<Finding>>(
            File.ReadAllText(Path.Combine(dir, "raw-findings.json")), CouncilJson.Options)!;
        var observations = JsonSerializer.Deserialize<List<EngineeringObservation>>(
            File.ReadAllText(Path.Combine(dir, "observations.json")), CouncilJson.Options)!;

        return Reconciler.Reconcile(raw, observations);
    }

    [Fact]
    public void Replay_29_to_27_with_exactly_two_dedup_merges()
    {
        var result = Replay();
        var summary = result.Summary;

        Assert.Equal(29, summary.RawFindingCount);
        Assert.Equal(27, summary.ConsolidatedFindingCount);
        Assert.Equal(29, summary.PreDedupFindingCount);      // without the dedup stage
        Assert.Equal(27, summary.PostDedupFindingCount);     // after the dedup stage
        Assert.Equal(2, summary.DeduplicatedFindingCount);   // one per dedup join
        Assert.Equal(2, result.ConsolidatedFindings.Count(f => f.IsConsolidated));
        Assert.All(result.ConsolidatedFindings.Where(f => f.IsConsolidated),
            f => Assert.Equal("dedup-identity", f.ReconciliationStrategy));
    }

    [Fact]
    public void Replay_RequestFilter_NRE_reliability_pair_merges()
    {
        var result = Replay();
        var merged = Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated
            && f.SupportingFindingIds.Contains("RAW-012"));
        Assert.Contains("RAW-016", merged.SupportingFindingIds);
        Assert.Equal(2, merged.SupportingFindingIds.Count);
        Assert.True(merged.AgreementCount >= 2);          // providers span the two sources
        Assert.Contains("OpenCode", merged.SupportingProviders);
        Assert.Contains("ClaudeCode", merged.SupportingProviders);
    }

    [Fact]
    public void Replay_wallet_httpclient_pair_merges()
    {
        var result = Replay();
        var merged = Assert.Single(result.ConsolidatedFindings, f => f.IsConsolidated
            && f.SupportingFindingIds.Contains("RAW-011"));
        Assert.Contains("RAW-014", merged.SupportingFindingIds);
        Assert.Equal(2, merged.SupportingFindingIds.Count);
        Assert.True(merged.AgreementCount >= 2);
        Assert.Contains("OpenCode", merged.SupportingProviders);
        Assert.Contains("Codex", merged.SupportingProviders);
        Assert.Equal("Medium–High", merged.SeverityRange);  // severity spread recorded
    }

    [Fact]
    public void Replay_health_endpoint_pair_stays_separate()
    {
        var result = Replay();
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-028");
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-029");
    }

    [Fact]
    public void Replay_magic_string_trio_stays_separate()
    {
        var result = Replay();
        foreach (var rawId in new[] { "RAW-001", "RAW-003", "RAW-005" })
            Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == rawId);
    }

    [Fact]
    public void Replay_contextlogger_and_cross_discipline_views_stay_separate()
    {
        var result = Replay();
        // ContextLogger finding is never absorbed into the RequestFilter NRE merge.
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-017");
        // Observability and code-quality views of the same RequestFilter defect remain
        // their own discipline findings (cross-discipline barrier, fixture H).
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-026");
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-004");
        // Broad-catch finding stays separate from the NRE merge (title focus differs).
        Assert.Single(result.ConsolidatedFindings, f => f.SupportingFindingIds.Count == 1 && f.SupportingFindingIds[0] == "RAW-015");
    }

    [Fact]
    public void Replay_is_deterministic_across_reversed_input()
    {
        var dir = PersistedDir();
        var raw = JsonSerializer.Deserialize<List<Finding>>(
            File.ReadAllText(Path.Combine(dir, "raw-findings.json")), CouncilJson.Options)!;
        var observations = JsonSerializer.Deserialize<List<EngineeringObservation>>(
            File.ReadAllText(Path.Combine(dir, "observations.json")), CouncilJson.Options)!;

        var forward = Reconciler.Reconcile(raw, observations);
        var reversed = Reconciler.Reconcile(raw.Reverse<Finding>().ToList(), observations);

        Assert.Equal(
            forward.ConsolidatedFindings.Select(f => f.Id),
            reversed.ConsolidatedFindings.Select(f => f.Id));
        Assert.Equal(forward.Summary.DeduplicatedFindingCount, reversed.Summary.DeduplicatedFindingCount);
    }
}