using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3C — repository snapshot identity: read-only Git metadata,
/// start-before-acquisition capture, single end-of-run mutation verification,
/// non-Git robustness, and the compact Markdown provenance block.
///
/// All tests are OFFLINE over local fixture directories (temporary repositories
/// created and mutated here only). No network, no provider invocation, no
/// credentials. Git-dependent tests soft-skip when git is unavailable.
/// </summary>
public sealed class RepositorySnapshotIdentityTests : IDisposable
{
    private readonly List<string> _roots = [];
    private readonly FileSystemRepositoryScanner _scanner = new();
    private readonly RepositorySnapshotIdentityProvider _provider = new(new FileSystemRepositoryScanner());

    [Fact]
    public async Task Clean_git_metadata_captured_correctly()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");

        var identity = await Capture(root);

        Assert.Equal("git", identity.VersionControl);
        Assert.Equal(Git(root, "rev-parse", "HEAD"), identity.CommitSha);
        Assert.Equal(Git(root, "symbolic-ref", "--short", "HEAD"), identity.Branch);
        Assert.False(identity.IsDirty);
        Assert.False(identity.HasUntrackedFiles);
        Assert.False(string.IsNullOrEmpty(identity.SnapshotFingerprint));
    }

    [Fact]
    public async Task Same_head_dirty_tracked_file_changes_fingerprint_and_sets_dirty()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        var clean = await Capture(root);

        File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "code v2");

        var dirty = await Capture(root);
        Assert.Equal(clean.CommitSha, dirty.CommitSha);   // HEAD unchanged
        Assert.True(dirty.IsDirty);
        Assert.NotEqual(clean.SnapshotFingerprint, dirty.SnapshotFingerprint);
    }

    [Fact]
    public async Task Same_head_untracked_source_file_changes_fingerprint_and_is_detected()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        var clean = await Capture(root);

        var untrackedDir = Path.Combine(root, "src", "Extra");
        Directory.CreateDirectory(untrackedDir);
        File.WriteAllText(Path.Combine(untrackedDir, "NewFile.cs"), "new relevant source");

        var with = await Capture(root);
        Assert.Equal(clean.CommitSha, with.CommitSha);    // HEAD unchanged
        Assert.True(with.HasUntrackedFiles);
        Assert.NotEqual(clean.SnapshotFingerprint, with.SnapshotFingerprint);
    }

    [Fact]
    public async Task Dirty_state_detected()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        Assert.False((await Capture(root)).IsDirty);
        File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "modified");
        Assert.True((await Capture(root)).IsDirty);
    }

    [Fact]
    public async Task Untracked_state_detected()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        Assert.False((await Capture(root)).HasUntrackedFiles);
        File.WriteAllText(Path.Combine(root, "src", "Untracked.cs"), "untracked");
        Assert.True((await Capture(root)).HasUntrackedFiles);
    }

    [Fact]
    public async Task Detached_head_reports_null_branch_but_commit()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        Git(root, "checkout", "--detach");

        var identity = await Capture(root);
        Assert.Null(identity.Branch);
        Assert.NotNull(identity.CommitSha);
        Assert.Equal(Git(root, "rev-parse", "HEAD"), identity.CommitSha);
    }

    [Fact]
    public async Task Non_git_repository_still_gets_fingerprint()
    {
        var root = CreateDir();
        File.WriteAllText(Path.Combine(root, "Program.cs"), "no git here");

        var identity = await Capture(root);

        Assert.Null(identity.VersionControl);
        Assert.Null(identity.CommitSha);
        Assert.Null(identity.Branch);
        Assert.Null(identity.IsDirty);
        Assert.Null(identity.HasUntrackedFiles);
        Assert.False(string.IsNullOrEmpty(identity.SnapshotFingerprint));
    }

    [Fact]
    public async Task Git_unavailable_does_not_fail_analysis()
    {
        var root = CreateDir();
        File.WriteAllText(Path.Combine(root, "Program.cs"), "plain source");

        var provider = new RepositorySnapshotIdentityProvider(new FileSystemRepositoryScanner(), "definitely-not-a-real-git-xyz");
        var snapshot = await _scanner.ScanAsync(root, new ScanOptions());
        var identity = await provider.CaptureAsync(snapshot, root, new ScanOptions());

        Assert.Null(identity.VersionControl);
        Assert.Null(identity.CommitSha);
        Assert.Null(identity.Branch);
        Assert.Null(identity.IsDirty);
        Assert.Null(identity.HasUntrackedFiles);
        Assert.False(string.IsNullOrEmpty(identity.SnapshotFingerprint)); // fingerprint still works
    }

    [Fact]
    public async Task Git_unavailable_with_git_directory_still_identifies_version_control()
    {
        var root = CreateDir();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "Program.cs"), "plain source");

        var provider = new RepositorySnapshotIdentityProvider(new FileSystemRepositoryScanner(), "definitely-not-a-real-git-xyz");
        var snapshot = await _scanner.ScanAsync(root, new ScanOptions());
        var identity = await provider.CaptureAsync(snapshot, root, new ScanOptions());

        Assert.Equal("git", identity.VersionControl); // positively identified by .git
        Assert.Null(identity.CommitSha);              // git unavailable → metadata null
        Assert.Null(identity.Branch);
        Assert.Null(identity.IsDirty);
        Assert.Null(identity.HasUntrackedFiles);
        Assert.False(string.IsNullOrEmpty(identity.SnapshotFingerprint));
    }

    [Fact]
    public async Task Start_equals_end_reports_no_change()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        var start = await Capture(root);

        var changed = await _provider.ChangedDuringRunAsync(root, new ScanOptions(), start.SnapshotFingerprint!);

        Assert.False(changed);
    }

    [Fact]
    public async Task Start_not_equal_end_reports_change_and_start_fingerprint_is_retained()
    {
        if (!GitAvailable()) return;
        var root = InitRepo("code v1");
        var start = await Capture(root);
        var startFingerprint = start.SnapshotFingerprint!;

        File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "code v2");

        var changed = await _provider.ChangedDuringRunAsync(root, new ScanOptions(), startFingerprint);

        Assert.True(changed);
        // The start identity is immutable and was never replaced by the end state.
        Assert.Equal(startFingerprint, start.SnapshotFingerprint);
        Assert.False(start.RepositoryChangedDuringRun);
    }

    [Fact]
    public async Task End_verification_scan_without_content_loads_uses_same_selection()
    {
        var root = CreateDir();
        File.WriteAllText(Path.Combine(root, "Program.cs"), "class Program { }");
        File.WriteAllText(Path.Combine(root, "App.sln"), "solution");

        var withContent = await _scanner.ScanAsync(root, new ScanOptions());
        var withoutContent = await _scanner.ScanAsync(root, new ScanOptions { LoadContent = false });

        Assert.Equal(
            withContent.Files.Select(f => f.RelativePath).OrderBy(x => x),
            withoutContent.Files.Select(f => f.RelativePath).OrderBy(x => x));
        Assert.Equal(withContent.SolutionName, withoutContent.SolutionName);
        Assert.All(withoutContent.Files, f => Assert.Null(f.Content)); // not loaded
        Assert.Equal(
            EngineeringCouncil.Core.Analysis.SnapshotFingerprint.Compute(root, withContent.Files),
            EngineeringCouncil.Core.Analysis.SnapshotFingerprint.Compute(root, withoutContent.Files));
    }

    // ── Markdown provenance block ─────────────────────────────────────────────

    [Fact]
    public void Markdown_renders_git_clean_repository_state()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(Package(new RepositorySnapshotIdentity
        {
            VersionControl = "git",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            Branch = "master",
            IsDirty = false,
            HasUntrackedFiles = false,
            SnapshotFingerprint = "SNAP-abcdef012345",
            RepositoryChangedDuringRun = false
        }));

        Assert.Contains("## Repository State", md);
        Assert.Contains("- **Commit:** `0123456789ab...`", md);
        Assert.Contains("- **Branch:** master", md);
        Assert.Contains("- **Working tree:** clean", md);
        Assert.Contains("- **Untracked analyzed files:** No", md);
        Assert.Contains("- **Snapshot:** `SNAP-abcdef012345`", md);
        Assert.Contains("- **Changed during review:** No", md);
    }

    [Fact]
    public void Markdown_renders_dirty_changed_repository_state()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(Package(new RepositorySnapshotIdentity
        {
            VersionControl = "git",
            CommitSha = "0123456789abcdef0123456789abcdef01234567",
            Branch = "master",
            IsDirty = true,
            HasUntrackedFiles = true,
            SnapshotFingerprint = "SNAP-abcdef012345",
            RepositoryChangedDuringRun = true
        }));

        Assert.Contains("- **Working tree:** modified", md);
        Assert.Contains("- **Untracked analyzed files:** Yes", md);
        Assert.Contains("- **Changed during review:** Yes", md);
    }

    [Fact]
    public void Markdown_renders_non_git_repository_state()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(Package(new RepositorySnapshotIdentity
        {
            VersionControl = null,
            SnapshotFingerprint = "SNAP-abcdef012345",
            RepositoryChangedDuringRun = false
        }));

        Assert.Contains("## Repository State", md);
        Assert.Contains("- **Version control identity:** unavailable", md);
        Assert.Contains("- **Snapshot:** `SNAP-abcdef012345`", md);
        // The git-specific lines do NOT appear in the Repository State block.
        Assert.DoesNotContain("- **Working tree:**", md);
        Assert.DoesNotContain("- **Changed during review:**", md);
    }

    [Fact]
    public void Markdown_omits_repository_state_when_no_identity()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(new EngineeringReviewPackage
        {
            Repository = "R",
            AnalysisRunId = "run1"
        });
        Assert.DoesNotContain("Repository State", md);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<RepositorySnapshotIdentity> Capture(string root)
    {
        var snapshot = await _scanner.ScanAsync(root, new ScanOptions());
        return await _provider.CaptureAsync(snapshot, root, new ScanOptions());
    }

    private string InitRepo(string content)
    {
        var root = CreateDir();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "Program.cs"), content);
        Git(root, "init");
        Git(root, "config", "user.email", "test@example.com");
        Git(root, "config", "user.name", "Council Test");
        Git(root, "add", ".");
        Git(root, "commit", "-m", "fixture");
        return root;
    }

    private string CreateDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "ec-rid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private static string Git(string root, params string[] args)
    {
        var info = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }

    private static bool GitAvailable()
    {
        try
        {
            return Git(Path.GetTempPath(), "--version").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static EngineeringReviewPackage Package(RepositorySnapshotIdentity identity)
        => new()
        {
            Repository = "R",
            AnalysisRunId = "run1",
            RepositorySnapshot = identity
        };

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}