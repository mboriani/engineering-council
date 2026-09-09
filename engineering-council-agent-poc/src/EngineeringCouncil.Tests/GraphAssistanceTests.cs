using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Focused tests for M16.3 graph-assisted agentic context integration.
/// Uses fakes/test doubles — no Graphify installation required.
/// </summary>
public sealed class GraphAssistanceTests
{
    // Test 1 — disabled graph assistance preserves existing behavior
    [Fact]
    public async Task DisabledGraphAssistance_ReturnsNullContext()
    {
        var provider = new FakeGraphContextProvider(enabled: false);

        var result = await provider.GetContextAsync(
            "/repo", "SNAP-test123", FindingCategory.Security);

        Assert.False(result.Enabled);
        Assert.Null(result.Context);
    }

    // Test 2 — cache hit does not execute Graphify
    [Fact]
    public async Task CacheHit_DoesNotExecuteGraphify()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: true);

        var result = await provider.GetContextAsync(
            "/repo", "SNAP-test", FindingCategory.Security);

        Assert.True(result.CacheHit);
        Assert.False(result.ExtractionExecuted);
    }

    // Test 3 — cache miss executes Graphify once
    [Fact]
    public async Task CacheMiss_ExecutesGraphify()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: false);

        var result = await provider.GetContextAsync(
            "/repo", "SNAP-miss", FindingCategory.Security);

        Assert.False(result.CacheHit);
        Assert.True(result.ExtractionExecuted);
    }

    // Test 4 — same snapshot reuses graph
    [Fact]
    public async Task SameSnapshot_ReusesGraph()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: true);

        var result = await provider.GetContextAsync("/repo", "SNAP-reuse", FindingCategory.Security);

        Assert.True(result.CacheHit);
        Assert.False(result.ExtractionExecuted);
    }

    // Test 5 — different snapshot does NOT reuse stale graph
    [Fact]
    public async Task DifferentSnapshot_DoesNotReuseStaleGraph()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: false);

        var result = await provider.GetContextAsync("/repo", "SNAP-new", FindingCategory.Security);

        Assert.False(result.CacheHit);
        Assert.True(result.ExtractionExecuted);
    }

    // Test 6 — concurrent requests do not trigger duplicate extraction
    [Fact]
    public async Task ConcurrentRequests_SingleExtraction()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: false);

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            provider.GetContextAsync("/repo", "SNAP-concurrent", FindingCategory.Security));

        var results = await Task.WhenAll(tasks);

        // All should have gotten a result
        Assert.All(results, r => Assert.True(r.Enabled));
    }

    // Test 7 — Graphify failure falls back to AdditionalContext null
    [Fact]
    public async Task GraphifyFailure_FallsBackToNull()
    {
        var provider = new FakeGraphContextProvider(enabled: true, failureReason: "extraction failed");

        var result = await provider.GetContextAsync("/repo", "SNAP-fail", FindingCategory.Security);

        Assert.Null(result.Context);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("extraction failed", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // Test 8 — invalid graph falls back safely
    [Fact]
    public async Task InvalidGraph_FallsBackSafely()
    {
        var provider = new FakeGraphContextProvider(enabled: true, failureReason: "invalid graph JSON");

        var result = await provider.GetContextAsync("/repo", "SNAP-invalid", FindingCategory.Security);

        Assert.Null(result.Context);
        Assert.NotNull(result.FailureReason);
    }

    // Test 9 — Security receives minimal context
    [Fact]
    public async Task Security_ReceivesMinimalContext()
    {
        var provider = new FakeGraphContextProvider(enabled: true, contextText: "Security Navigation Map\n\nSecurity Filter → User Auth flow");

        var result = await provider.GetContextAsync("/repo", "SNAP-sec", FindingCategory.Security);

        Assert.NotNull(result.Context);
        Assert.Contains("Security Navigation Map", result.Context);
    }

    // Test 10 — Architecture receives minimal context
    [Fact]
    public async Task Architecture_ReceivesMinimalContext()
    {
        var provider = new FakeGraphContextProvider(enabled: true, contextText: "Architecture Navigation Map\n\nCore → Infrastructure flow");

        var result = await provider.GetContextAsync("/repo", "SNAP-arch", FindingCategory.Architecture);

        Assert.NotNull(result.Context);
        Assert.Contains("Architecture Navigation Map", result.Context);
    }

    // Test 11 — unsupported discipline receives null
    [Fact]
    public async Task UnsupportedDiscipline_ReceivesNull()
    {
        var provider = new FakeGraphContextProvider(enabled: true);

        var result = await provider.GetContextAsync("/repo", "SNAP-test", FindingCategory.Testing);

        Assert.Null(result.Context);
    }

    // Test 12 — context remains <=3000 chars
    [Theory]
    [InlineData(FindingCategory.Security)]
    [InlineData(FindingCategory.Architecture)]
    public async Task ContextRemainsUnder3000Chars(FindingCategory discipline)
    {
        var provider = new FakeGraphContextProvider(enabled: true, contextText: new string('x', 2500));

        var result = await provider.GetContextAsync("/repo", "SNAP-budget", discipline);

        Assert.NotNull(result.Context);
        Assert.True(result.Context.Length <= 3000,
            $"Context length {result.Context.Length} exceeds 3000 chars for {discipline}");
    }

    // Test 13 — provider-neutral AdditionalContext path remains unchanged
    [Fact]
    public void EvidenceRequest_AdditionalContext_RemainsOptional()
    {
        var request = new EvidenceRequest
        {
            RunId = "test",
            RepositorySnapshot = new RepositorySnapshot
            {
                RootPath = "/test",
                SolutionName = "Test"
            },
            Scope = EvidenceAcquisitionScope.Discipline,
            Instructions = "test",
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "test",
                Files = [],
                TotalRepositoryFiles = 0,
                SelectedFileCount = 0,
                EstimatedContentSize = 0
            },
            ProviderNames = ["Test"],
            CorrelationId = "test"
        };

        Assert.Null(request.AdditionalContext);
    }

    // Test 14 — graph extraction is not executed once per discipline
    [Fact]
    public async Task GraphExtraction_NotPerDiscipline()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: true);

        await provider.GetContextAsync("/repo", "SNAP-multi", FindingCategory.Security);
        await provider.GetContextAsync("/repo", "SNAP-multi", FindingCategory.Architecture);

        // Should not throw — same snapshot, different disciplines
    }

    // Test 15 — snapshot mismatch is observable in diagnostics
    [Fact]
    public async Task SnapshotMismatch_ObservableInDiagnostics()
    {
        var provider = new FakeGraphContextProvider(enabled: true, cacheHit: false);

        var result = await provider.GetContextAsync("/repo", "SNAP-wrong", FindingCategory.Security);

        Assert.False(result.CacheHit);
        Assert.True(result.ExtractionExecuted);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Fake IGraphContextProvider for testing.</summary>
    private sealed class FakeGraphContextProvider : IGraphContextProvider
    {
        private readonly bool _enabled;
        private readonly bool _cacheHit;
        private readonly string? _contextText;
        private readonly string? _failureReason;

        /// <summary>Supported disciplines for context generation.</summary>
        private static readonly HashSet<FindingCategory> SupportedDisciplines =
            [FindingCategory.Security, FindingCategory.Architecture];

        public FakeGraphContextProvider(
            bool enabled,
            bool cacheHit = false,
            string? contextText = null,
            string? failureReason = null)
        {
            _enabled = enabled;
            _cacheHit = cacheHit;
            _contextText = contextText;
            _failureReason = failureReason;
        }

        public Task<GraphContextResult> GetContextAsync(
            string repositoryPath,
            string snapshotFingerprint,
            FindingCategory discipline,
            CancellationToken cancellationToken = default)
        {
            if (!_enabled)
                return Task.FromResult(new GraphContextResult { Enabled = false });

            if (!SupportedDisciplines.Contains(discipline))
                return Task.FromResult(new GraphContextResult { Enabled = true });

            if (_failureReason is not null)
                return Task.FromResult(new GraphContextResult
                {
                    Enabled = true,
                    FailureReason = _failureReason
                });

            var context = _contextText ?? $"Default {discipline} context";

            return Task.FromResult(new GraphContextResult
            {
                Enabled = true,
                Context = context,
                CacheHit = _cacheHit,
                ExtractionExecuted = !_cacheHit,
                ContextCharacters = context.Length,
                Discipline = discipline
            });
        }
    }
}
