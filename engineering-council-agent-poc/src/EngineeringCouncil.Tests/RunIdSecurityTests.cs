using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reporting;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 011.1 — run ids are used as output folder names, so the filesystem
/// repository must never let an untrusted run id escape the outputs root. Defense
/// in depth: strict format validation (the canonical generated shape has no path
/// separators), full-path resolution, and a verification that the resolved path
/// stays below OutputsRoot.
/// </summary>
public sealed class RunIdSecurityTests : IDisposable
{
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-runsec-" + Guid.NewGuid().ToString("N"));

    private FileSystemAnalysisRunRepository Repo()
        => new(new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
            new EngineeringReviewMarkdownExporter(), new JsonReportGenerator());

    [Fact]
    public void Generated_run_ids_match_the_canonical_format()
    {
        var id = AnalysisRunId.New();

        Assert.Matches(@"^[0-9]{8}-[0-9]{6}-[0-9a-f]{6}$", id);
        Assert.True(AnalysisRunId.IsValid(id));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:\\Windows")]
    [InlineData("C:/Windows")]
    [InlineData("/etc/passwd")]
    [InlineData("nested/run")]
    [InlineData("nested\\run")]
    [InlineData("run.json")]
    [InlineData("")]
    [InlineData("not-a-run-id")]
    [InlineData("20240101-120000-abcDEF")]
    public async Task Malformed_or_traversal_run_ids_are_rejected_as_not_found(string runId)
    {
        var run = await Repo().GetAsync(runId);

        Assert.Null(run);
    }

    [Fact]
    public async Task A_valid_run_id_round_trips_and_resolves_inside_the_outputs_root()
    {
        var repo = Repo();
        var run = new AnalysisRun { RunId = AnalysisRunId.New(), TargetPath = "/repo", SolutionName = "Sample" };
        var package = new EngineeringReviewPackageBuilder().Build(run);

        var dir = await repo.SaveAsync(run, package);

        Assert.StartsWith(Path.GetFullPath(_outputs), dir);

        var loaded = await repo.GetAsync(run.RunId);
        Assert.NotNull(loaded);
        Assert.Equal(run.RunId, loaded!.RunId);
        Assert.Equal("Sample", loaded.SolutionName);
    }

    [Fact]
    public async Task A_traversal_id_can_never_read_a_run_planted_outside_the_outputs_root()
    {
        var sibling = Path.Combine(Path.GetTempPath(), "ec-sibling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "run.json"), "stolen");
        try
        {
            var run = await Repo().GetAsync("..\\..\\" + Path.GetFileName(sibling) + "\\run.json");
            Assert.Null(run);
        }
        finally
        {
            try { Directory.Delete(sibling, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task SaveAsync_rejects_a_run_id_that_escapes_the_outputs_root()
    {
        var repo = Repo();
        var run = new AnalysisRun { RunId = "../../evil", TargetPath = "/repo" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => repo.SaveAsync(run, new EngineeringReviewPackageBuilder().Build(run)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_outputs, recursive: true); } catch { /* best effort */ }
    }
}
