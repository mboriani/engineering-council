using System.Text.Json;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 010 — the engineering-review-package.json integration contract for the
/// external Engineering Review application: consolidated findings, schema version,
/// backward compatibility, no leaked .NET types, deterministic serialization, and a
/// consumer DTO that must deserialize the package. Also generates the preserved
/// integration sample.
/// </summary>
public sealed class PackageContractTests
{
    // ── Representative run fixture (single-source, multi-source, SARIF-supported) ──

    private static AnalysisRun BuildRun()
    {
        // Observations from two providers; SARIF-backed ones carry a SourceEvidenceId.
        var observations = new List<EngineeringObservation>
        {
            new() { Id = "OBS-001", Discipline = FindingCategory.Security, ObservationType = ObservationTypes.HardcodedSecret,
                Title = "Hardcoded secret", SourceProvider = "Claude", RuleId = "CWE-798",
                FileReferences = [new FileReference { Path = "src/Config.cs", StartLine = 42 }], LineReferences = [42] },
            new() { Id = "OBS-002", Discipline = FindingCategory.Security, ObservationType = ObservationTypes.HardcodedSecret,
                Title = "Hardcoded secret", SourceProvider = "SARIF", SourceEvidenceId = "ev-sarif", RuleId = "CWE-798",
                FileReferences = [new FileReference { Path = "src/Config.cs", StartLine = 42 }], LineReferences = [42] },
            new() { Id = "OBS-003", Discipline = FindingCategory.Reliability, ObservationType = ObservationTypes.MissingTimeout,
                Title = "Missing timeout", SourceProvider = "SARIF", SourceEvidenceId = "ev-sarif", RuleId = "REL-001",
                FileReferences = [new FileReference { Path = "src/Http.cs", StartLine = 10 }], LineReferences = [10] },
            new() { Id = "OBS-004", Discipline = FindingCategory.Documentation, ObservationType = ObservationTypes.MissingDocumentation,
                Title = "Missing docs", SourceProvider = "Claude", FileReferences = [new FileReference { Path = "README.md" }] },
        };

        var rawFindings = new List<Finding>
        {
            Raw("RAW-001", FindingCategory.Security, "Hardcoded secret in configuration", "Claude",
                FindingSeverity.High, FindingConfidence.Medium, ["CWE-798"], "src/Config.cs", 42, ["OBS-001"]),
            Raw("RAW-002", FindingCategory.Security, "Hardcoded credential in config", "SARIF",
                FindingSeverity.High, FindingConfidence.High, ["CWE-798"], "src/Config.cs", 42, ["OBS-002"]),
            Raw("RAW-003", FindingCategory.Reliability, "Missing timeout on HTTP call", "SARIF",
                FindingSeverity.Medium, FindingConfidence.High, ["REL-001"], "src/Http.cs", 10, ["OBS-003"]),
            Raw("RAW-004", FindingCategory.Documentation, "Missing XML documentation", "Claude",
                FindingSeverity.Low, FindingConfidence.Medium, [], "README.md", 0, ["OBS-004"]),
        };

        var reconciliation = new RuleBasedFindingReconciler().Reconcile(rawFindings, observations);

        var plan = new EvidenceAcquisitionPlan
        {
            RunId = "sample-run",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Steps =
            [
                new EvidenceAcquisitionStep { StepId = "Claude#Security", ProviderName = "Claude", Scope = EvidenceAcquisitionScope.Discipline,
                    Discipline = FindingCategory.Security, InstructionsReference = "Security", ContextSelectionStrategy = "security-focused-v1", CorrelationId = "Claude:Security" },
                new EvidenceAcquisitionStep { StepId = "SARIF#repository", ProviderName = "SARIF", Scope = EvidenceAcquisitionScope.Repository,
                    InstructionsReference = "repository", ContextSelectionStrategy = "repository-wide", CorrelationId = "SARIF:repository" },
            ]
        };
        var records = new List<ProviderExecutionRecord>
        {
            new() { RunId = "sample-run", StepId = "Claude#Security", ProviderName = "Claude", ProviderType = EvidenceProviderType.LLM,
                Scope = EvidenceAcquisitionScope.Discipline, RequestedDiscipline = FindingCategory.Security, Success = true, EvidenceCount = 1, ContextFileCount = 12, ContextFilesConsidered = 120 },
            new() { RunId = "sample-run", StepId = "SARIF#repository", ProviderName = "SARIF", ProviderType = EvidenceProviderType.StaticAnalyzer,
                Scope = EvidenceAcquisitionScope.Repository, Success = true, EvidenceCount = 1, ContextFileCount = 0, ContextFilesConsidered = 120 },
        };
        var report = ProviderExecutionReport.FromRecords(records, plan);

        return new AnalysisRun
        {
            RunId = "sample-run",
            TargetPath = "/repo",
            SolutionName = "SampleSolution",
            Branch = "main",
            Commit = "abc1234",
            RepositoryIdentity = new RepositorySnapshotIdentity
            {
                VersionControl = "git",
                CommitSha = "0123456789abcdef0123456789abcdef01234567",
                Branch = "main",
                IsDirty = false,
                HasUntrackedFiles = false,
                SnapshotFingerprint = "SNAP-abcdef012345",
                RepositoryChangedDuringRun = false
            },
            Projects = 3,
            FilesScanned = 120,
            Provider = "Claude, SARIF",
            Status = AnalysisRunStatus.Completed,
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(5),
            RequestedDisciplines = [FindingCategory.Security, FindingCategory.Reliability, FindingCategory.Documentation],
            AcquisitionPlan = plan,
            ProviderExecution = report,
            Evidence =
            [
                new Evidence { Id = "ev-sarif", ProviderName = "SARIF", ProviderType = EvidenceProviderType.StaticAnalyzer, Success = true,
                    Metadata = new Dictionary<string, string> { ["tool"] = "ExampleScanner", ["toolVersion"] = "3.1.0", ["resultCount"] = "2", ["format"] = "sarif" } },
            ],
            Observations = observations,
            ObservationInterpretationSummary = new ObservationInterpretationSummary { ObservationsProduced = observations.Count },
            RawFindings = rawFindings,
            Findings = reconciliation.ConsolidatedFindings,
            ReconciliationSummary = reconciliation.Summary,
            ReconciliationGroups = reconciliation.Groups,
        };
    }

    private static Finding Raw(string id, FindingCategory disc, string title, string provider,
        FindingSeverity sev, FindingConfidence conf, string[] rules, string file, int line, string[] obsIds)
        => new()
        {
            Id = id, Title = title, Category = disc, Severity = sev, Confidence = conf,
            Summary = title, Description = title + " details.", Recommendation = "Remediate.",
            EvidenceProvider = provider, SupportingProviders = [provider], SourceRules = rules,
            FileReferences = line > 0 ? [new FileReference { Path = file, StartLine = line }] : [new FileReference { Path = file }],
            ObservationIds = obsIds, SupportingObservationCount = obsIds.Length, SourceAgent = disc.ToString().ToLowerInvariant() + "-analyzer",
        };

    private static EngineeringReviewPackage BuildPackage()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildRun());
        // Deterministic timestamps/durations for a stable sample artifact.
        return package with
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            AnalysisDuration = TimeSpan.FromSeconds(5),
            Reconciliation = package.Reconciliation is { } r ? r with { Duration = TimeSpan.Zero } : null
        };
    }

    // ── M15.3D: minimal run whose reconciler deduplicates exactly one pair ─────

    private static AnalysisRun BuildDedupRun()
    {
        var observations = new List<EngineeringObservation>
        {
            new() { Id = "OBS-001", Discipline = FindingCategory.Reliability, SourceProvider = "OpenCode",
                Title = "RequestFilter dereferences null telemetry after catch", SymbolReferences = ["OnActionExecutionAsync"],
                FileReferences = [new FileReference { Path = "src/RequestFilter.cs", StartLine = 35 }], LineReferences = [35] },
            new() { Id = "OBS-002", Discipline = FindingCategory.Reliability, SourceProvider = "ClaudeCode",
                Title = "Null telemetry object dereferenced causing NullReferenceException", SymbolReferences = ["RequestFilter.OnActionExecutionAsync"],
                FileReferences = [new FileReference { Path = "src/RequestFilter.cs", StartLine = 37 }], LineReferences = [37] },
        };
        var rawFindings = new List<Finding>
        {
            Raw("RAW-001", FindingCategory.Reliability, "RequestFilter dereferences null telemetry after catch", "OpenCode",
                FindingSeverity.High, FindingConfidence.High, [], "src/RequestFilter.cs", 35, ["OBS-001"]),
            Raw("RAW-002", FindingCategory.Reliability, "Null telemetry object dereferenced causing NullReferenceException", "ClaudeCode",
                FindingSeverity.High, FindingConfidence.High, [], "src/RequestFilter.cs", 37, ["OBS-002"]),
        };
        var reconciliation = new RuleBasedFindingReconciler().Reconcile(rawFindings, observations);

        return new AnalysisRun
        {
            RunId = "dedup-run",
            TargetPath = "/repo",
            SolutionName = "SampleSolution",
            Branch = "main",
            Commit = "abc1234",
            Provider = "OpenCode, ClaudeCode",
            Status = AnalysisRunStatus.Completed,
            Observations = observations,
            RawFindings = rawFindings,
            Findings = reconciliation.ConsolidatedFindings,
            ReconciliationSummary = reconciliation.Summary,
            ReconciliationGroups = reconciliation.Groups
        };
    }

    // ── 19. schema version emitted ────────────────────────────────────────────

    [Fact]
    public void Package_emits_schema_version()
    {
        Assert.Equal("1.1", BuildPackage().SchemaVersion);
        Assert.Contains("\"schemaVersion\": \"1.1\"", JsonSerializer.Serialize(BuildPackage(), CouncilJson.Options));
    }

    // ── 14. package uses consolidated findings ────────────────────────────────

    [Fact]
    public void Package_uses_consolidated_findings_not_raw()
    {
        var package = BuildPackage();
        Assert.Equal(3, package.Findings.Count);                         // 4 raw → 3 consolidated
        Assert.Contains(package.Findings, f => f.IsConsolidated && f.AgreementCount == 2);
        Assert.Equal(4, package.Appendix.RawFindings.Count);            // raw preserved in appendix
    }

    // ── 15/16. findings.json = consolidated, raw-findings.json = raw ──────────

    [Fact]
    public void Findings_json_is_consolidated_and_raw_json_is_raw()
    {
        var run = BuildRun();
        var json = new JsonReportGenerator();
        var findings = JsonSerializer.Deserialize<List<Finding>>(json.RenderFindings(run), CouncilJson.Options)!;
        var raw = JsonSerializer.Deserialize<List<Finding>>(json.RenderRawFindings(run), CouncilJson.Options)!;

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f.IsConsolidated);
        Assert.Equal(4, raw.Count);
        Assert.All(raw, f => Assert.False(f.IsConsolidated));           // raw untouched
        Assert.Equal(["RAW-001", "RAW-002", "RAW-003", "RAW-004"], raw.Select(f => f.Id).OrderBy(x => x));
    }

    // ── 20. backward compatibility (legacy fields retained) ───────────────────

    [Fact]
    public void Package_remains_backward_compatible()
    {
        var json = JsonSerializer.Serialize(BuildPackage(), CouncilJson.Options);
        Assert.Contains("\"version\": \"1.0\"", json);   // legacy consumers still see version
        Assert.Contains("\"findings\":", json);
        Assert.Contains("\"metrics\":", json);
        Assert.Contains("\"evidenceSummary\":", json);
    }

    // ── no internal runtime types leak + deterministic serialization ──────────

    [Fact]
    public void Serialization_leaks_no_dotnet_types_and_is_deterministic()
    {
        var package = BuildPackage();
        var a = JsonSerializer.Serialize(package, CouncilJson.Options);
        var b = JsonSerializer.Serialize(package, CouncilJson.Options);
        Assert.Equal(a, b);                                              // deterministic serialization
        foreach (var leak in new[] { "$type", "System.", "PublicKeyToken", "Culture=", "mscorlib", "EngineeringCouncil.Core" })
            Assert.DoesNotContain(leak, a);
        // Enums serialize as camelCase strings, not integers.
        Assert.Contains("\"severity\": \"high\"", a);
        Assert.Contains("\"category\": \"security\"", a);
    }

    // ── 21/23. consumer DTO deserializes the package ──────────────────────────

    [Fact]
    public void Consumer_contract_can_deserialize_the_package()
    {
        var json = JsonSerializer.Serialize(BuildPackage(), CouncilJson.Options);
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;

        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.Equal("SampleSolution", dto.Repository);
        Assert.False(string.IsNullOrWhiteSpace(dto.ExecutiveSummary));
        Assert.Equal(3, dto.Findings.Count);

        var multi = Assert.Single(dto.Findings, f => f.IsConsolidated && f.AgreementCount == 2);
        Assert.Equal("security", multi.Category);
        Assert.Contains("Claude", multi.SupportingProviders);
        Assert.Contains("SARIF", multi.SupportingProviders);
        Assert.NotEmpty(multi.ObservationIds);

        Assert.NotNull(dto.Reconciliation);
        Assert.Equal(4, dto.Reconciliation!.RawFindingCount);
        Assert.Equal(3, dto.Reconciliation.ConsolidatedFindingCount);
        Assert.Equal(1, dto.Reconciliation.MultiProviderFindingCount);
        Assert.Equal(2, dto.Reconciliation.FindingsByProvider["Claude"]);

        var source = Assert.Single(dto.StaticAnalysisSources);
        Assert.Equal("ExampleScanner", source.Tool);
        Assert.Equal(2, source.ImportedResults);

        Assert.NotNull(dto.AcquisitionCoverage);
        Assert.Contains("Security", dto.AcquisitionCoverage!.DisciplinesRequested);

        // M15.3C — the external consumer can read the reproducibility identity.
        Assert.NotNull(dto.RepositorySnapshot);
        Assert.Equal("git", dto.RepositorySnapshot!.VersionControl);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", dto.RepositorySnapshot.CommitSha);
        Assert.Equal("main", dto.RepositorySnapshot.Branch);
        Assert.False(dto.RepositorySnapshot.IsDirty);
        Assert.False(dto.RepositorySnapshot.HasUntrackedFiles);
        Assert.Equal("SNAP-abcdef012345", dto.RepositorySnapshot.SnapshotFingerprint);
        Assert.False(dto.RepositorySnapshot.RepositoryChangedDuringRun);
    }

    // ── M15.3C: repository snapshot identity serializes additively ──────────

    [Fact]
    public void Package_exposes_repository_snapshot_identity_without_paths()
    {
        var package = BuildPackage();
        Assert.NotNull(package.RepositorySnapshot);

        var json = JsonSerializer.Serialize(package, CouncilJson.Options);
        Assert.Contains("\"repositorySnapshot\":", json);
        Assert.Contains("\"snapshotFingerprint\": \"SNAP-abcdef012345\"", json);
        Assert.Contains("\"repositoryChangedDuringRun\": false", json);
        Assert.Equal("1.1", package.SchemaVersion);   // additive — schema unchanged

        // Privacy: no absolute local path is exposed through the package.
        Assert.DoesNotContain("C:", json);
        Assert.DoesNotContain("/repo", json);
    }

    // ── 17. markdown renders provider support ─────────────────────────────────

    [Fact]
    public void Markdown_renders_multi_source_reconciliation_and_provider_support()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(BuildPackage());
        Assert.Contains("## Multi-Source Reconciliation", md);
        Assert.Contains("Multi-provider findings:", md);
        Assert.Contains("Claude", md);
        Assert.Contains("SARIF", md);
        Assert.Contains("agreement", md);
    }

    // ── M15.3D: deterministic dedup diagnostics ride the reconciliation contract ──

    [Fact]
    public void Package_exposes_dedup_diagnostics_additively_and_keeps_schema()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildDedupRun());
        Assert.Equal("1.1", package.SchemaVersion);                       // additive — schema unchanged
        Assert.NotNull(package.Reconciliation);
        Assert.Equal(2, package.Reconciliation!.PreDedupFindingCount);
        Assert.Equal(1, package.Reconciliation.PostDedupFindingCount);
        Assert.Equal(1, package.Reconciliation.DeduplicatedFindingCount);

        // The consumer DTO round-trips the additive dedup fields.
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.NotNull(dto.Reconciliation);
        Assert.Equal(2, dto.Reconciliation!.PreDedupFindingCount);
        Assert.Equal(1, dto.Reconciliation.PostDedupFindingCount);
        Assert.Equal(1, dto.Reconciliation.DeduplicatedFindingCount);
    }

    [Fact]
    public void Markdown_renders_compact_dedup_line_when_dedup_occurred()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildDedupRun());
        var md = new EngineeringReviewMarkdownExporter().Export(package);
        Assert.Contains("## Multi-Source Reconciliation", md);
        Assert.Contains("- **Deduplicated findings:** 1 (pre-dedup 2 → post-dedup 1)", md);
    }

    [Fact]
    public void Markdown_omits_dedup_line_when_no_dedup_occurred()
    {
        var md = new EngineeringReviewMarkdownExporter().Export(BuildPackage());
        Assert.DoesNotContain("Deduplicated findings", md);
    }

    // ── Integration sample generation (required artifact) ─────────────────────

    [Fact]
    public void Generates_and_validates_the_integration_sample()
    {
        var package = BuildPackage();
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);

        // The sample must round-trip through the external consumer contract.
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;
        Assert.Equal(3, dto.Findings.Count);

        // Preserve it under artifacts/samples when the repo tree is locatable.
        var repoRoot = FindRepoRoot();
        if (repoRoot is not null)
        {
            var dir = Path.Combine(repoRoot, "artifacts", "samples");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "engineering-review-package.sample.json"), json);
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }
}
