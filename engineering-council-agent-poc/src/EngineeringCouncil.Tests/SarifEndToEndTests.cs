using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// End-to-end validation of the native SARIF source through the real DI graph:
/// repository scanned once, SARIF executed once, SARIF results become observations
/// that the EXISTING analyzers consume unmodified, and the Engineering Review
/// Package is produced with the Static Analysis Sources appendix and full
/// Finding → Observation → Evidence → SARIF rule traceability.
/// </summary>
public sealed class SarifEndToEndTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-sarif-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-sarif-out-" + Guid.NewGuid().ToString("N"));
    private readonly string _sarif = Path.Combine(Path.GetTempPath(), $"ec-e2e-{Guid.NewGuid():N}.sarif");

    public SarifEndToEndTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Db.cs"), "public class Db { }\n");
        File.WriteAllText(_sarif, """
        {
          "version": "2.1.0",
          "runs": [
            {
              "tool": { "driver": { "name": "SecScan", "version": "4.5.6", "rules": [
                { "id": "CWE-89", "name": "SqlInjection", "shortDescription": { "text": "SQL injection." },
                  "helpUri": "https://example.com/cwe-89", "properties": { "tags": ["security"] } }
              ] } },
              "artifacts": [ { "location": { "uri": "src/Db.cs" } } ],
              "invocations": [ { "executionSuccessful": true } ],
              "results": [
                { "ruleId": "CWE-89", "ruleIndex": 0, "level": "error",
                  "message": { "text": "SQL injection via concatenation." },
                  "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "src/Db.cs" },
                    "region": { "startLine": 3, "startColumn": 1 } } } ] }
              ]
            }
          ]
        }
        """);
    }

    [Fact]
    public async Task Sarif_source_flows_end_to_end_without_touching_analyzers()
    {
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddEngineeringCouncil(o =>
            {
                o.OutputsRoot = _outputs;
                o.Providers = ["Sarif"];
                o.SarifEnabled = true;
                o.SarifFiles = [_sarif];
            })
            .BuildServiceProvider();

        var pipeline = sp.GetRequiredService<AnalysisPipeline>();
        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "SARIF" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);

        // Repository scanned once; SARIF executed exactly once (Repository scope).
        Assert.NotNull(result.Run.AcquisitionPlan);
        Assert.Single(result.Run.AcquisitionPlan!.Steps);
        Assert.Equal(1, result.Run.ProviderExecution!.TotalExecutions);

        // Observations came from SARIF.
        var observation = Assert.Single(result.Run.Observations);
        Assert.Equal("SARIF", observation.SourceProvider);
        Assert.Equal(FindingCategory.Security, observation.Discipline);
        Assert.Equal("CWE-89", observation.RuleId);

        // The EXISTING security analyzer consumed the SARIF observation → a finding.
        var finding = Assert.Single(result.Run.Findings, f => f.Category == FindingCategory.Security);
        Assert.Contains(observation.Id, finding.ObservationIds);        // Finding → Observation
        Assert.Contains("SARIF", finding.SupportingProviders);          // → Evidence provider

        // Package carries the Static Analysis Sources metric.
        var source = Assert.Single(result.Package.StaticAnalysisSources);
        Assert.Equal("SecScan", source.Tool);
        Assert.Equal("4.5.6", source.Version);
        Assert.Equal(1, source.ImportedResults);
        Assert.Equal(1, source.GeneratedObservations);

        // Markdown includes the appendix; the written file exists.
        var md = new EngineeringReviewMarkdownExporter().Export(result.Package);
        Assert.Contains("## Static Analysis Sources", md);
        Assert.Contains("SecScan", md);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "engineering-review.md")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "observations.json")));
    }

    public void Dispose()
    {
        try { File.Delete(_sarif); } catch { /* best effort */ }
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
