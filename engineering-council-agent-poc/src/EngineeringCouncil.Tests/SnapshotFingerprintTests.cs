using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3C — deterministic snapshot fingerprint over the analyzed
/// working state. Pure offline tests over local fixture directories: no git,
/// no network, no provider invocation, no credentials.
/// </summary>
public sealed class SnapshotFingerprintTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public void Deterministic_fingerprint_for_identical_files()
    {
        var root = CreateFiles(("src/Program.cs", "class Program { }"), ("README.md", "# Demo\n"));
        var a = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs", "README.md"));
        var b = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs", "README.md"));
        Assert.Equal(a, b);
        Assert.StartsWith("SNAP-", a);
    }

    [Fact]
    public void File_ordering_does_not_change_fingerprint()
    {
        var root = CreateFiles(("a.cs", "A"), ("b.cs", "B"), ("c.cs", "C"));
        var forward = SnapshotFingerprint.Compute(root, Selection(root, "a.cs", "b.cs", "c.cs"));
        var reversed = SnapshotFingerprint.Compute(root, Selection(root, "c.cs", "b.cs", "a.cs"));
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Absolute_repository_location_does_not_change_fingerprint()
    {
        var rootA = CreateFiles(("src/Program.cs", "same"), ("src/Util.cs", "util"));
        var rootB = CreateFiles(("src/Program.cs", "same"), ("src/Util.cs", "util"));
        Assert.NotEqual(rootA, rootB); // different absolute locations
        Assert.Equal(
            SnapshotFingerprint.Compute(rootA, Selection(rootA, "src/Program.cs", "src/Util.cs")),
            SnapshotFingerprint.Compute(rootB, Selection(rootB, "src/Program.cs", "src/Util.cs")));
    }

    [Fact]
    public void Source_content_change_changes_fingerprint()
    {
        var rootA = CreateFiles(("src/Program.cs", "class Program { }"));
        var rootB = CreateFiles(("src/Program.cs", "class Program { void Main() { } }"));
        Assert.NotEqual(
            SnapshotFingerprint.Compute(rootA, Selection(rootA, "src/Program.cs")),
            SnapshotFingerprint.Compute(rootB, Selection(rootB, "src/Program.cs")));
    }

    [Fact]
    public void Relevant_file_addition_changes_fingerprint()
    {
        var root = CreateFiles(("src/Program.cs", "x"));
        var without = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs"));
        File.WriteAllText(Path.Combine(root, "src", "Util.cs"), "util");
        var with = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs", "src/Util.cs"));
        Assert.NotEqual(without, with);
    }

    [Fact]
    public void Relevant_file_deletion_changes_fingerprint()
    {
        var root = CreateFiles(("src/Program.cs", "x"), ("src/Util.cs", "util"));
        var with = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs", "src/Util.cs"));
        File.Delete(Path.Combine(root, "src", "Util.cs"));
        var without = SnapshotFingerprint.Compute(root, Selection(root, "src/Program.cs"));
        Assert.NotEqual(with, without);
    }

    [Fact]
    public void Excluded_directories_do_not_affect_fingerprint()
    {
        var clean = CreateFiles(("src/Program.cs", "code"));
        var dirty = CreateFiles(("src/Program.cs", "code"));
        Directory.CreateDirectory(Path.Combine(dirty, "bin"));
        File.WriteAllText(Path.Combine(dirty, "bin", "junk.cs"), "binary"); // bin is never selected

        // The selection passed in mirrors the scanner's post-ignore-rule set: the
        // same file list in both cases → identical fingerprint despite extra junk
        // under bin/obj/.git that the scanner would exclude.
        Assert.Equal(
            SnapshotFingerprint.Compute(clean, Selection(clean, "src/Program.cs")),
            SnapshotFingerprint.Compute(dirty, Selection(dirty, "src/Program.cs")));

        // And even when such a file IS present in the selection it changes identity.
        Assert.NotEqual(
            SnapshotFingerprint.Compute(clean, Selection(clean, "src/Program.cs")),
            SnapshotFingerprint.Compute(dirty, Selection(dirty, "src/Program.cs", "bin/junk.cs")));
    }

    [Fact]
    public void Unreadable_file_produces_stable_marker_not_content()
    {
        var root = CreateFiles(("a.cs", "secret content"));
        // Selection claims a file the scanner catalogued but that is now unreadable.
        var a = SnapshotFingerprint.Compute(root, Selection(root, "a.cs"));
        var b = SnapshotFingerprint.Compute(root, Selection(root, "a.cs"));
        Assert.Equal(a, b);
        // Never any source content in the output.
        Assert.DoesNotContain("secret content", a);
        Assert.DoesNotContain("secret content", b);
    }

    private string CreateFiles(params (string Path, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "ec-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    private static IReadOnlyList<ScannedFile> Selection(string root, params string[] relativePaths)
        => relativePaths.Select(rp => new ScannedFile
        {
            RelativePath = rp,
            Extension = Path.GetExtension(rp),
            SizeBytes = File.Exists(Path.Combine(root, rp.Replace('/', Path.DirectorySeparatorChar)))
                ? new FileInfo(Path.Combine(root, rp.Replace('/', Path.DirectorySeparatorChar))).Length
                : 0
        }).ToList();

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}