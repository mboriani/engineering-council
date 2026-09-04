using System.Text.Json;
using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 010 — end-to-end multi-source reconciliation (scenario 22 + final
/// acceptance gate). Two Claude-/Codex-style LLM test doubles and the native SARIF
/// source flow through the REAL pipeline; equivalent security findings are
/// reconciled into one consolidated, multi-provider finding; the package builder
/// receives the consolidated findings; and the written engineering-review-package.json
/// is accepted by the external consumer contract.
/// </summary>
public sealed class ReconciliationEndToEndTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-recon-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-recon-out-" + Guid.NewGuid().ToString("N"));
    private readonly string _sarif = Path.Combine(Path.GetTempPath(), $"ec-recon-{Guid.NewGuid():N}.sarif");

    public ReconciliationEndToEndTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Secrets.cs"), "public class Secrets { const string Key = \"abc\"; }\n");
        File.WriteAllText(_sarif, """
        {
          "version": "2.1.0",
          "runs": [ {
            "tool": { "driver": { "name": "SecScan", "version": "2.0", "rules": [
              { "id": "CWE-798", "name": "HardcodedCredential", "shortDescription": { "text": "Hardcoded credential." },
                "properties": { "tags": ["security"] } } ] } },
            "results": [ { "ruleId": "CWE-798", "ruleIndex": 0, "level": "error",
              "message": { "text": "Hardcoded credential in Secrets.cs." },
              "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "src/Secrets.cs" }, "region": { "startLine": 1 } } } ] } ]
          } ]
        }
        """);
    }

    [Fact]
    public async Task Claude_codex_and_sarif_findings_reconcile_end_to_end()
    {
        // Two LLM doubles report the SAME security issue (rule CWE-798, same file) but
        // as DIFFERENT observation types, so the analyzers emit separate raw findings
        // that only the reconciler (stage 1: shared rule + location) can consolidate.
        var providers = new IEvidenceProvider[]
        {
            new SecretsLlmProvider("Claude", "HardcodedSecret"),
            new SecretsLlmProvider("Codex", "MissingAuthorization"),
            new SarifEvidenceProvider(new SarifOptions { Enabled = true, Files = [_sarif] }),
        };
        var factory = new EvidenceProviderFactory(providers);
        var interpretation = new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
            [new StructuredLlmEvidenceInterpreter(), new SarifEvidenceInterpreter()]));
        var orchestrator = new AnalysisOrchestrator([new SecurityAnalyzer(), new ReliabilityAnalyzer(), new CodeQualityAnalyzer()]);
        var repo = new FileSystemAnalysisRunRepository(
            new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
            new EngineeringReviewMarkdownExporter(), new JsonReportGenerator());

        var pipeline = new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), new EvidenceOptions { Providers = ["Claude", "Codex", "SARIF"] }),
            interpretation, orchestrator,
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            new EvidenceOptions { Providers = ["Claude", "Codex", "SARIF"] },
            repo);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude, Codex, SARIF" });

        Assert.Equal(AnalysisRunStatus.Completed, result.Run.Status);

        // SARIF executed once (Repository scope); the two LLM sources ran per discipline.
        Assert.Equal(1, result.Run.ProviderExecution!.Records.Count(r => r.ProviderName == "SARIF"));

        // Raw findings are preserved and reconciliation produced consolidated findings.
        Assert.NotEmpty(result.Run.RawFindings);
        Assert.NotNull(result.Run.ReconciliationSummary);

        // The security issue is corroborated by multiple providers.
        var security = Assert.Single(result.Run.Findings, f => f.Category == FindingCategory.Security);
        Assert.True(security.AgreementCount >= 2, $"expected ≥2 providers, got {security.AgreementCount}");
        Assert.True(security.IsConsolidated);
        Assert.Contains("SARIF", security.SupportingProviders);
        Assert.Contains(security.SupportingProviders, p => p is "Claude" or "Codex");

        // The package uses consolidated findings and carries the reconciliation summary.
        Assert.Equal(result.Run.Findings.Count, result.Package.Findings.Count);
        Assert.NotNull(result.Package.Reconciliation);
        Assert.Equal(result.Run.RawFindings.Count, result.Package.Reconciliation!.RawFindingCount);

        // The written engineering-review-package.json is accepted by the external contract.
        var packageJson = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.NotNull(dto.Reconciliation);
        Assert.Contains(dto.Findings, f => f.IsConsolidated && f.AgreementCount >= 2);

        // Diagnostic artifacts remain, but the package is self-sufficient.
        foreach (var file in new[] { "engineering-review-package.json", "engineering-review.md", "findings.json", "raw-findings.json" })
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, file)));
    }

    /// <summary>An LLM-style provider that reports one security observation (of the given type) for src/Secrets.cs.</summary>
    private sealed class SecretsLlmProvider(string name, string observationType) : IEvidenceProvider
    {
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequiresAnalyzerInstructions = true
        };

        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            var json = request.Discipline == FindingCategory.Security
                ? $$"""
                  { "observations": [ {
                    "type": "{{observationType}}", "discipline": "Security",
                    "title": "Hardcoded secret in Secrets.cs", "description": "A constant credential is embedded in source.",
                    "severity": "High", "confidence": "Medium", "ruleId": "CWE-798",
                    "fileReferences": [ { "path": "src/Secrets.cs", "startLine": 1 } ],
                    "evidenceExcerpt": "const string Key = \"abc\";" } ] }
                  """
                : """{ "observations": [] }""";

            IReadOnlyList<Evidence> result =
            [
                new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.LLM, RawResponse = json, Success = true }
            ];
            return Task.FromResult(result);
        }
    }

    public void Dispose()
    {
        try { File.Delete(_sarif); } catch { /* best effort */ }
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
