using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Reporting;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 009 — native SARIF evidence source. Proves a real deterministic
/// provider participates in the pipeline without touching analyzers/interpreters/
/// package: SARIF import (provider), result→observation mapping (interpreter),
/// package metrics + markdown appendix, and full end-to-end traceability.
/// </summary>
public sealed class SarifTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    // ── A representative, valid SARIF 2.1.0 payload ───────────────────────────
    private static string Sarif(
        string tool = "TestAnalyzer",
        string version = "1.2.3",
        string ruleId = "SEC001",
        string ruleName = "SqlInjection",
        string level = "error",
        string uri = "src/Db.cs",
        int startLine = 12,
        int startColumn = 7,
        string tag = "security",
        string message = "Possible SQL injection via string concatenation.")
        => $$"""
        {
          "version": "2.1.0",
          "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
          "runs": [
            {
              "tool": {
                "driver": {
                  "name": "{{tool}}",
                  "version": "{{version}}",
                  "informationUri": "https://example.com/{{tool}}",
                  "rules": [
                    {
                      "id": "{{ruleId}}",
                      "name": "{{ruleName}}",
                      "shortDescription": { "text": "Detects {{ruleName}} issues." },
                      "helpUri": "https://example.com/rules/{{ruleId}}",
                      "properties": { "tags": ["{{tag}}"], "precision": "high" },
                      "defaultConfiguration": { "level": "{{level}}" }
                    }
                  ]
                }
              },
              "artifacts": [ { "location": { "uri": "{{uri}}" } } ],
              "invocations": [ { "executionSuccessful": true, "commandLine": "testanalyzer scan" } ],
              "results": [
                {
                  "ruleId": "{{ruleId}}",
                  "ruleIndex": 0,
                  "level": "{{level}}",
                  "message": { "text": "{{message}}" },
                  "locations": [
                    {
                      "physicalLocation": {
                        "artifactLocation": { "uri": "{{uri}}" },
                        "region": { "startLine": {{startLine}}, "startColumn": {{startColumn}}, "endLine": {{startLine}}, "snippet": { "text": "var q = \"...\" + input;" } }
                      }
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private string WriteSarif(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ec-sarif-{Guid.NewGuid():N}.sarif");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static EvidenceRequest RepositoryRequest()
    {
        var snapshot = new RepositorySnapshot { RootPath = "/r", SolutionName = "R" };
        return new EvidenceRequest
        {
            RunId = "run1",
            RepositorySnapshot = snapshot,
            Scope = EvidenceAcquisitionScope.Repository,
            Discipline = null,
            Instructions = "repository",
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "repository-wide", Files = [], TotalRepositoryFiles = 0,
                SelectedFileCount = 0, EstimatedContentSize = 0
            },
            ProviderNames = ["SARIF"],
            CorrelationId = "SARIF:repository"
        };
    }

    private static SarifEvidenceProvider Provider(params string[] files)
        => new(new SarifOptions { Enabled = true, Files = files });

    private static async Task<IReadOnlyList<EngineeringObservation>> Interpret(Evidence evidence)
        => await new SarifEvidenceInterpreter().InterpretAsync(
            evidence, new AnalyzerContext { Snapshot = new RepositorySnapshot { RootPath = "/r", SolutionName = "R" } });

    // ── Provider ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Valid_sarif_produces_one_evidence_preserving_tool_metadata_and_payload()
    {
        var evidence = Assert.Single(await Provider(WriteSarif(Sarif())).CollectAsync(RepositoryRequest()));

        Assert.Equal("SARIF", evidence.ProviderName);
        Assert.Equal(EvidenceProviderType.StaticAnalyzer, evidence.ProviderType);
        Assert.Equal("TestAnalyzer", evidence.Metadata["tool"]);
        Assert.Equal("1.2.3", evidence.Metadata["toolVersion"]);
        Assert.Equal("1", evidence.Metadata["resultCount"]);
        Assert.Equal("1", evidence.Metadata["artifactCount"]);
        Assert.Equal("true", evidence.Metadata["invocationSuccessful"]);
        Assert.Contains("SqlInjection", evidence.RawResponse); // original payload preserved
    }

    [Fact]
    public async Task Malformed_sarif_is_skipped_without_throwing()
    {
        var evidence = await Provider(WriteSarif("{ this is not valid json")).CollectAsync(RepositoryRequest());
        Assert.Empty(evidence);
    }

    [Fact]
    public async Task Empty_sarif_run_produces_evidence_with_zero_results()
    {
        var empty = """{ "version": "2.1.0", "runs": [ { "tool": { "driver": { "name": "Empty" } }, "results": [] } ] }""";
        var evidence = Assert.Single(await Provider(WriteSarif(empty)).CollectAsync(RepositoryRequest()));

        Assert.Equal("0", evidence.Metadata["resultCount"]);
        Assert.Empty(await Interpret(evidence));
    }

    [Fact]
    public async Task Multiple_runs_produce_one_evidence_each()
    {
        var twoRuns = """
        { "version": "2.1.0", "runs": [
          { "tool": { "driver": { "name": "A", "version": "1" } }, "results": [] },
          { "tool": { "driver": { "name": "B", "version": "2" } }, "results": [] }
        ] }
        """;
        var evidence = await Provider(WriteSarif(twoRuns)).CollectAsync(RepositoryRequest());

        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, e => e.Metadata["tool"] == "A");
        Assert.Contains(evidence, e => e.Metadata["tool"] == "B");
    }

    [Fact]
    public async Task Multiple_files_are_all_imported()
    {
        var files = new[] { WriteSarif(Sarif(tool: "First")), WriteSarif(Sarif(tool: "Second")) };
        var evidence = await Provider(files).CollectAsync(RepositoryRequest());

        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, e => e.Metadata["tool"] == "First");
        Assert.Contains(evidence, e => e.Metadata["tool"] == "Second");
    }

    [Fact]
    public void Provider_is_repository_scoped_and_available_only_with_files()
    {
        Assert.Equal(EvidenceAcquisitionScope.Repository, Provider(WriteSarif(Sarif())).Metadata.DefaultAcquisitionScope);
        Assert.True(Provider(WriteSarif(Sarif())).IsAvailable);
        Assert.False(new SarifEvidenceProvider(new SarifOptions { Enabled = true, Files = [] }).IsAvailable);
        Assert.False(new SarifEvidenceProvider(new SarifOptions { Enabled = false, Files = ["x.sarif"] }).IsAvailable);
    }

    [Fact]
    public async Task Repository_scoped_sarif_executes_once_regardless_of_disciplines()
    {
        var provider = Provider(WriteSarif(Sarif()));
        var factory = new EvidenceProviderFactory([provider]);
        FindingCategory[] allDisciplines =
        [
            FindingCategory.Architecture, FindingCategory.Security, FindingCategory.Reliability,
            FindingCategory.CodeQuality, FindingCategory.Testing, FindingCategory.Documentation, FindingCategory.Observability
        ];
        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "run1" },
            new RepositorySnapshot { RootPath = "/r", SolutionName = "R" }, [provider], allDisciplines);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EvidenceAcquisitionScope.Repository, step.Scope);

        var result = await new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), new EvidenceOptions())
            .ExecuteAsync(plan, new RepositorySnapshot { RootPath = "/r", SolutionName = "R" });
        Assert.Single(result.Executions);
        Assert.Equal(1, result.Executions[0].EvidenceCount);
    }

    // ── Interpreter ───────────────────────────────────────────────────────────

    [Fact]
    public void Interpreter_only_claims_sarif_static_evidence()
    {
        var interpreter = new SarifEvidenceInterpreter();
        Assert.True(interpreter.CanInterpret(new Evidence
        {
            ProviderName = "SARIF", ProviderType = EvidenceProviderType.StaticAnalyzer, Success = true,
            Metadata = new Dictionary<string, string> { ["format"] = "sarif" }
        }));
        Assert.False(interpreter.CanInterpret(new Evidence
        {
            ProviderName = "Claude", ProviderType = EvidenceProviderType.LLM, Success = true
        }));
    }

    [Fact]
    public async Task Result_maps_to_observation_discipline_type_and_severity()
    {
        // A secret-detection rule maps to both the Security discipline and the
        // HardcodedSecret observation type, deterministically.
        var evidence = Assert.Single(await Provider(WriteSarif(
            Sarif(ruleId: "SEC-SECRET", ruleName: "HardcodedPassword", level: "error", tag: "security",
                  message: "Hardcoded password detected."))).CollectAsync(RepositoryRequest()));
        var observation = Assert.Single(await Interpret(evidence));

        Assert.Equal(FindingCategory.Security, observation.Discipline);        // security tag
        Assert.Equal(FindingSeverity.Critical, observation.Severity);          // error → Critical
        Assert.Equal(ObservationTypes.HardcodedSecret, observation.ObservationType); // "password" signal
    }

    [Theory]
    [InlineData("error", FindingSeverity.Critical)]
    [InlineData("warning", FindingSeverity.High)]
    [InlineData("note", FindingSeverity.Medium)]
    [InlineData("none", FindingSeverity.Low)]
    public async Task Severity_mapping_is_complete(string level, FindingSeverity expected)
    {
        var evidence = Assert.Single(await Provider(WriteSarif(Sarif(level: level))).CollectAsync(RepositoryRequest()));
        var observation = Assert.Single(await Interpret(evidence));
        Assert.Equal(expected, observation.Severity);
    }

    [Fact]
    public async Task Discipline_mapping_falls_back_to_unknown_for_unrecognized_rules()
    {
        var evidence = Assert.Single(await Provider(WriteSarif(
            Sarif(ruleId: "XYZ", ruleName: "Frobnicate", tag: "misc", message: "Something happened."))).CollectAsync(RepositoryRequest()));
        var observation = Assert.Single(await Interpret(evidence));

        Assert.Equal(FindingCategory.Unknown, observation.Discipline);
        Assert.Equal(ObservationTypes.Unknown, observation.ObservationType);
    }

    [Fact]
    public async Task Full_metadata_yields_high_confidence_and_incomplete_yields_lower()
    {
        var full = Assert.Single(await Provider(WriteSarif(Sarif())).CollectAsync(RepositoryRequest()));
        Assert.Equal(FindingConfidence.High, (await Interpret(full)).Single().Confidence);

        // No location, no message, unknown level → several deficiencies → Low.
        var poor = """
        { "version": "2.1.0", "runs": [ { "tool": { "driver": { "name": "T" } },
          "results": [ { "ruleId": "R", "locations": [] } ] } ] }
        """;
        var poorEvidence = Assert.Single(await Provider(WriteSarif(poor)).CollectAsync(RepositoryRequest()));
        Assert.Equal(FindingConfidence.Low, (await Interpret(poorEvidence)).Single().Confidence);
    }

    [Fact]
    public async Task Observation_carries_line_column_file_and_rule_metadata()
    {
        var evidence = Assert.Single(await Provider(WriteSarif(Sarif(uri: "src/Db.cs", startLine: 12, startColumn: 7))).CollectAsync(RepositoryRequest()));
        var observation = Assert.Single(await Interpret(evidence));

        Assert.Equal("src/Db.cs", Assert.Single(observation.FileReferences).Path);
        Assert.Contains(12, observation.LineReferences);
        Assert.Contains(7, observation.ColumnReferences);
        Assert.Equal("SEC001", observation.RuleId);
        Assert.Equal("SqlInjection", observation.Metadata["ruleName"]);
        Assert.Equal("https://example.com/rules/SEC001", observation.Metadata["helpUri"]);
        Assert.Contains("security", observation.Metadata["tags"]);
        Assert.False(string.IsNullOrEmpty(observation.EvidenceExcerpt));
    }

    [Fact]
    public async Task Rule_is_resolved_by_index_when_ruleId_absent_on_result()
    {
        var byIndex = """
        { "version": "2.1.0", "runs": [ { "tool": { "driver": { "name": "T",
          "rules": [ { "id": "R100", "name": "Complexity", "properties": { "tags": ["maintainability"] } } ] } },
          "results": [ { "ruleIndex": 0, "level": "warning", "message": { "text": "complex method" },
            "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "a.cs" }, "region": { "startLine": 1 } } } ] } ] } ] }
        """;
        var evidence = Assert.Single(await Provider(WriteSarif(byIndex)).CollectAsync(RepositoryRequest()));
        var observation = Assert.Single(await Interpret(evidence));

        Assert.Equal("R100", observation.RuleId);
        Assert.Equal(FindingCategory.CodeQuality, observation.Discipline);
    }

    [Fact]
    public async Task Multiple_results_produce_multiple_observations_with_provenance()
    {
        var multi = """
        { "version": "2.1.0", "runs": [ { "tool": { "driver": { "name": "T", "version": "9" } },
          "results": [
            { "ruleId": "A1", "level": "warning", "message": { "text": "auth issue" }, "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "a.cs" }, "region": { "startLine": 1 } } } ] },
            { "ruleId": "B2", "level": "note", "message": { "text": "logging gap" }, "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "b.cs" }, "region": { "startLine": 2 } } } ] }
          ] } ] }
        """;
        var evidence = Assert.Single(await Provider(WriteSarif(multi)).CollectAsync(RepositoryRequest()));
        var observations = await Interpret(evidence);

        Assert.Equal(2, observations.Count);
        Assert.All(observations, o =>
        {
            Assert.Equal("SARIF", o.SourceProvider);
            Assert.Equal(EvidenceProviderType.StaticAnalyzer, o.SourceProviderType);
            Assert.Equal(evidence.Id, o.SourceEvidenceId);
        });
    }

    [Fact]
    public async Task Provenance_is_stamped_by_the_executor_and_carried_to_observations()
    {
        var provider = Provider(WriteSarif(Sarif()));
        var factory = new EvidenceProviderFactory([provider]);
        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "runX" },
            new RepositorySnapshot { RootPath = "/r", SolutionName = "R" }, [provider], [FindingCategory.Security]);
        var result = await new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), new EvidenceOptions())
            .ExecuteAsync(plan, new RepositorySnapshot { RootPath = "/r", SolutionName = "R" });

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(EvidenceAcquisitionScope.Repository, evidence.AcquisitionScope);
        Assert.False(string.IsNullOrEmpty(evidence.AcquisitionStepId));

        var observation = Assert.Single(await Interpret(evidence));
        Assert.Equal(evidence.AcquisitionStepId, observation.AcquisitionStepId);
        Assert.Equal(evidence.CorrelationId, observation.AcquisitionCorrelationId);
    }

    // ── Package metrics + markdown ────────────────────────────────────────────

    [Fact]
    public async Task StaticAnalysisSources_metric_counts_imported_results_and_observations()
    {
        var evidence = Assert.Single(await Provider(WriteSarif(Sarif())).CollectAsync(RepositoryRequest()));
        var stamped = evidence with { AcquisitionStepId = "SARIF#repository" };
        var observations = await Interpret(stamped);

        var sources = StaticAnalysisSource.From([stamped], observations);
        var source = Assert.Single(sources);
        Assert.Equal("TestAnalyzer", source.Tool);
        Assert.Equal("1.2.3", source.Version);
        Assert.Equal(1, source.ImportedResults);
        Assert.Equal(1, source.GeneratedObservations);
    }

    [Fact]
    public void Markdown_includes_the_static_analysis_sources_appendix()
    {
        var package = new EngineeringReviewPackage
        {
            Repository = "R",
            AnalysisRunId = "run1",
            StaticAnalysisSources = [new StaticAnalysisSource { Tool = "TestAnalyzer", Version = "1.2.3", ImportedResults = 3, GeneratedObservations = 3 }]
        };
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("## Static Analysis Sources", md);
        Assert.Contains("TestAnalyzer", md);
        Assert.Contains("| Tool | Version | Results Imported | Observations Generated |", md);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best effort */ }
    }
}
