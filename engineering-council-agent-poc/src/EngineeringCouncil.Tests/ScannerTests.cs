using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Scanning;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ec-scan-" + Guid.NewGuid().ToString("N"));

    public ScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));       // must be ignored
        Directory.CreateDirectory(Path.Combine(_root, "obj"));       // must be ignored
        Directory.CreateDirectory(Path.Combine(_root, "node_modules")); // must be ignored

        File.WriteAllText(Path.Combine(_root, "src", "Program.cs"), "class Program { }\n");
        File.WriteAllText(Path.Combine(_root, "App.sln"), "Microsoft Visual Studio Solution File\n");
        File.WriteAllText(Path.Combine(_root, "bin", "junk.cs"), "should be ignored");
        File.WriteAllText(Path.Combine(_root, "node_modules", "index.cs"), "should be ignored");
    }

    [Fact]
    public async Task Scan_ignores_excluded_directories_and_reads_source()
    {
        var scanner = new FileSystemRepositoryScanner();

        var snapshot = await scanner.ScanAsync(_root, new ScanOptions());

        Assert.DoesNotContain(snapshot.Files, f => f.RelativePath.Contains("bin/"));
        Assert.DoesNotContain(snapshot.Files, f => f.RelativePath.Contains("obj/"));
        Assert.DoesNotContain(snapshot.Files, f => f.RelativePath.Contains("node_modules/"));

        var program = Assert.Single(snapshot.Files, f => f.RelativePath == "src/Program.cs");
        Assert.False(string.IsNullOrEmpty(program.Content));
        Assert.Equal("App", snapshot.SolutionName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
