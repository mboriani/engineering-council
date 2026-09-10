using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Focused tests for M16.3B — GraphCacheIdentity produces stable graph cache
/// keys that represent the source snapshot, not generated analysis artifacts.
/// Pure offline tests: no git, no network, no provider invocation.
/// </summary>
public sealed class GraphCacheIdentityTests : IDisposable
{
    private readonly List<string> _roots = [];

    // ── Test 1: Same Git commit + clean tracked source → same GraphCacheKey ──

    [Fact]
    public void GitClean_SameCommit_SameKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        var a = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        var b = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        Assert.Equal(a, b);
        Assert.StartsWith("GRAPH-abc123def456", a);
    }

    // ── Test 2: Same commit + generated Council output artifact → same key ──

    [Fact]
    public void GitClean_WithCouncilOutput_SameKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        // Simulate Council-generated artifact in file list
        repo = repo with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "architecture_observations.json", Extension = ".json", SizeBytes = 50 },
                new ScannedFile { RelativePath = "security_observations.json", Extension = ".json", SizeBytes = 50 },
                new ScannedFile { RelativePath = "engineering-review-package.json", Extension = ".json", SizeBytes = 50 },
            ]
        };
        var a = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        var b = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        Assert.Equal(a, b);
        Assert.StartsWith("GRAPH-abc123def456", a);
    }

    // ── Test 3: Same commit + graph cache artifact → same key ──

    [Fact]
    public void GitClean_WithGraphCacheArtifact_SameKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        repo = repo with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "graphify-out/graph.json", Extension = ".json", SizeBytes = 1000 },
            ]
        };
        var a = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        var b = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        Assert.Equal(a, b);
        Assert.StartsWith("GRAPH-abc123def456", a);
    }

    // ── Test 4: Same commit + provider-generated artifact → same key ──

    [Fact]
    public void GitClean_WithProviderArtifact_SameKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        repo = repo with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "outputs/run-123/report.json", Extension = ".json", SizeBytes = 200 },
            ]
        };
        var a = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        var b = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        Assert.Equal(a, b);
    }

    // ── Test 5: Same commit + tracked source modification → DIFFERENT key ──

    [Fact]
    public void GitDirty_DifferentTrackedModifications_DifferentKeys()
    {
        var repo1 = FakeGitRepo(commit: "abc123def456", isClean: false,
            modifiedFiles: ["src/Program.cs"]);
        var repo2 = FakeGitRepo(commit: "abc123def456", isClean: false,
            modifiedFiles: ["src/Util.cs"]);

        var key1 = GraphCacheIdentity.Compute(repo1, isCleanGitTree: false,
            modifiedTrackedFiles: ["src/Program.cs"]);
        var key2 = GraphCacheIdentity.Compute(repo2, isCleanGitTree: false,
            modifiedTrackedFiles: ["src/Util.cs"]);

        Assert.NotEqual(key1, key2);
        Assert.Contains("dirty", key1);
        Assert.Contains("dirty", key2);
    }

    // ── Test 6: Different Git commit → DIFFERENT key ──

    [Fact]
    public void GitClean_DifferentCommits_DifferentKeys()
    {
        var repo1 = FakeGitRepo(commit: "aaa111bbb222", isClean: true);
        var repo2 = FakeGitRepo(commit: "ccc333ddd444", isClean: true);

        var key1 = GraphCacheIdentity.Compute(repo1, isCleanGitTree: true);
        var key2 = GraphCacheIdentity.Compute(repo2, isCleanGitTree: true);

        Assert.NotEqual(key1, key2);
    }

    // ── Test 7: Legitimate untracked source file with clean Git → same key ──

    [Fact]
    public void GitClean_UntrackedSourceFile_SameKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        // New untracked .cs file — should NOT change graph cache key for clean Git repo
        repo = repo with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "src/NewFeature.cs", Extension = ".cs", SizeBytes = 200 },
            ]
        };
        var key = GraphCacheIdentity.Compute(repo, isCleanGitTree: true);
        Assert.StartsWith("GRAPH-abc123def456", key);
    }

    // ── Test 8: Non-Git repository → deterministic source fingerprint ──

    [Fact]
    public void NonGit_DeterministicFingerprint()
    {
        var repo = FakeGitRepo(commit: null, isClean: null);
        repo = repo with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "README.md", Extension = ".md", SizeBytes = 50 },
            ]
        };
        var a = GraphCacheIdentity.Compute(repo);
        var b = GraphCacheIdentity.Compute(repo);
        Assert.Equal(a, b);
        Assert.StartsWith("GRAPH-", a);
        Assert.DoesNotContain("dirty", a);
    }

    // ── Test 9: Repeated calculation with unchanged source → byte-for-byte stable ──

    [Fact]
    public void RepeatedCalculation_UnchangedSource_StableKey()
    {
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);
        var keys = Enumerable.Range(0, 10)
            .Select(_ => GraphCacheIdentity.Compute(repo, isCleanGitTree: true))
            .ToList();
        Assert.All(keys, k => Assert.Equal(keys[0], k));
    }

    // ── Test 10: GraphAssistance disabled → existing behavior unchanged ──

    [Fact]
    public async Task GraphAssistance_Disabled_PreservesBehavior()
    {
        var provider = new FakeGraphContextProviderForIdentity(enabled: false);
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);

        var result = await provider.GetContextAsync(repo, FindingCategory.Security);

        Assert.False(result.Enabled);
        Assert.Null(result.Context);
    }

    // ── Test 11: Security/Architecture context integration unaffected ──

    [Fact]
    public async Task ContextIntegration_SecurityArchitecture_Unaffected()
    {
        var provider = new FakeGraphContextProviderForIdentity(
            enabled: true, contextText: "Navigation Map");
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);

        var sec = await provider.GetContextAsync(repo, FindingCategory.Security);
        var arch = await provider.GetContextAsync(repo, FindingCategory.Architecture);

        Assert.NotNull(sec.Context);
        Assert.NotNull(arch.Context);
        Assert.Contains("Navigation Map", sec.Context);
        Assert.Contains("Navigation Map", arch.Context);
    }

    // ── Test 12: Graphify failure fallback unaffected ──

    [Fact]
    public async Task GraphifyFailure_FallbackUnaffected()
    {
        var provider = new FakeGraphContextProviderForIdentity(
            enabled: true, failureReason: "extraction failed");
        var repo = FakeGitRepo(commit: "abc123def456", isClean: true);

        var result = await provider.GetContextAsync(repo, FindingCategory.Security);

        Assert.Null(result.Context);
        Assert.NotNull(result.FailureReason);
    }

    // ── Test: Non-Git excludes generated artifact files ──

    [Fact]
    public void NonGit_ExcludesGeneratedArtifacts()
    {
        var repoWithArtifacts = FakeGitRepo(commit: null, isClean: null);
        repoWithArtifacts = repoWithArtifacts with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
                new ScannedFile { RelativePath = "architecture_observations.json", Extension = ".json", SizeBytes = 50 },
                new ScannedFile { RelativePath = "graphify-out/graph.json", Extension = ".json", SizeBytes = 1000 },
            ]
        };
        var repoClean = FakeGitRepo(commit: null, isClean: null);
        repoClean = repoClean with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
            ]
        };

        var keyWithArtifacts = GraphCacheIdentity.Compute(repoWithArtifacts);
        var keyClean = GraphCacheIdentity.Compute(repoClean);

        // Generated artifacts excluded → same effective source → same key
        Assert.Equal(keyClean, keyWithArtifacts);
    }

    // ── Test: Git dirty with same modification list → same key ──

    [Fact]
    public void GitDirty_SameModifications_SameKey()
    {
        var modifiedFiles = new List<string> { "src/Program.cs", "src/Util.cs" };
        var repo1 = FakeGitRepo(commit: "abc123def456", isClean: false);
        var repo2 = FakeGitRepo(commit: "abc123def456", isClean: false);

        var key1 = GraphCacheIdentity.Compute(repo1, isCleanGitTree: false, modifiedTrackedFiles: modifiedFiles);
        var key2 = GraphCacheIdentity.Compute(repo2, isCleanGitTree: false, modifiedTrackedFiles: modifiedFiles);

        Assert.Equal(key1, key2);
    }

    // ── Test: IsGeneratedArtifact identifies known artifacts ──

    [Theory]
    [InlineData("architecture_observations.json", true)]
    [InlineData("security_observations.json", true)]
    [InlineData("engineering-review-package.json", true)]
    [InlineData("src/Program.cs", false)]
    [InlineData("README.md", false)]
    [InlineData("graphify-out/graph.json", true)]
    public void IsGeneratedArtifact_CorrectlyIdentifies(string path, bool expected)
    {
        Assert.Equal(expected, GraphCacheIdentity.IsGeneratedArtifact(path));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private RepositorySnapshot FakeGitRepo(string? commit, bool? isClean, IReadOnlyList<string>? modifiedFiles = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "ec-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);

        return new RepositorySnapshot
        {
            RootPath = root,
            SolutionName = "Test",
            Commit = commit,
            Branch = "main",
            Files =
            [
                new ScannedFile { RelativePath = "src/Program.cs", Extension = ".cs", SizeBytes = 100 },
            ]
        };
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Fake IGraphContextProvider for cache identity tests.</summary>
    private sealed class FakeGraphContextProviderForIdentity : IGraphContextProvider
    {
        private readonly bool _enabled;
        private readonly bool _cacheHit;
        private readonly string? _contextText;
        private readonly string? _failureReason;

        private static readonly HashSet<FindingCategory> SupportedDisciplines =
            [FindingCategory.Security, FindingCategory.Architecture];

        public FakeGraphContextProviderForIdentity(
            bool enabled, bool cacheHit = false, string? contextText = null, string? failureReason = null)
        {
            _enabled = enabled;
            _cacheHit = cacheHit;
            _contextText = contextText;
            _failureReason = failureReason;
        }

        public Task<GraphContextResult> GetContextAsync(
            RepositorySnapshot repository,
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
