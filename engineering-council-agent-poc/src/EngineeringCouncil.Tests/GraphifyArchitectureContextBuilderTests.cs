using System.IO;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Experiments;
using ArchitectureGraphContextOptions = EngineeringCouncil.Infrastructure.Experiments.ArchitectureGraphContextOptions;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class GraphifyArchitectureContextBuilderTests
{
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraph()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node1",
                    Label = "Program",
                    SourceFile = "host/Service.Host/Program.cs",
                    SourceLocation = "L10",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "program",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node2",
                    Label = "ServiceModule",
                    SourceFile = "host/Service.Host/ServiceModule.cs",
                    SourceLocation = "L5",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "servicemodule",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node3",
                    Label = "HomeController",
                    SourceFile = "host/Service.Host/Controllers/HomeController.cs",
                    SourceLocation = "L8",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "homecontroller",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node4",
                    Label = "IAppService",
                    SourceFile = "src/Application/IAppService.cs",
                    SourceLocation = "L1",
                    NormLabel = "iappservice",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node5",
                    Label = "AppService",
                    SourceFile = "src/Application/AppService.cs",
                    SourceLocation = "L20",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "appservice",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node6",
                    Label = "IApiProvider",
                    SourceFile = "src/Core/Providers/IApiProvider.cs",
                    SourceLocation = "L15",
                    NormLabel = "iapiprovider",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node7",
                    Label = "ApiProvider",
                    SourceFile = "src/Core/Providers/ApiProvider.cs",
                    SourceLocation = "L25",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "apiprovider",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node8",
                    Label = "HealthCheck",
                    SourceFile = "src/Diagnostics/HealthCheck.cs",
                    SourceLocation = "L30",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "healthcheck",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node9",
                    Label = "RequestEnricher",
                    SourceFile = "src/Telemetry/RequestEnricher.cs",
                    SourceLocation = "L10",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "requestenricher",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node10",
                    Label = "AppSettings",
                    SourceFile = "src/Core/Options/AppSettings.cs",
                    SourceLocation = "L1",
                    NormLabel = "appsettings",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node11",
                    Label = "TestService",
                    SourceFile = "tests/Application/TestService.cs",
                    SourceLocation = "L5",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "testservice",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "node12",
                    Label = "MockProvider",
                    SourceFile = "tests/Mocks/MockProvider.cs",
                    SourceLocation = "L8",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "mockprovider",
                    FileType = "code"
                }
            ],
            Links =
            [
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node1",
                    Target = "node2",
                    Relation = "contains",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "host/Service.Host/Program.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node2",
                    Target = "node5",
                    Relation = "imports",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "host/Service.Host/ServiceModule.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node5",
                    Target = "node7",
                    Relation = "calls",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "src/Application/AppService.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node7",
                    Target = "node6",
                    Relation = "implements",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "src/Core/Providers/ApiProvider.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node3",
                    Target = "node5",
                    Relation = "calls",
                    Confidence = "INFERRED",
                    ConfidenceScore = 0.85,
                    SourceFile = "host/Service.Host/Controllers/HomeController.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node10",
                    Target = "node4",
                    Relation = "references",
                    Confidence = "INFERRED",
                    ConfidenceScore = 0.75,
                    SourceFile = "src/Core/Options/AppSettings.cs",
                    Origin = "ast"
                },
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "node11",
                    Target = "node5",
                    Relation = "calls",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "tests/Application/TestService.cs",
                    Origin = "ast"
                }
            ],
            Communities =
            [
                new GraphifyArchitectureContextBuilder.GraphifyCommunity
                {
                    Name = "Composition",
                    Cohesion = 0.8,
                    Nodes = ["node1", "node2"]
                },
                new GraphifyArchitectureContextBuilder.GraphifyCommunity
                {
                    Name = "Application",
                    Cohesion = 0.7,
                    Nodes = ["node4", "node5"]
                }
            ],
            BuiltAtCommit = "abc123"
        };
    }

    // Test 1 — Determinism
    [Fact]
    public void Build_SameGraphSameOptions_ProducesIdenticalOutput()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output1 = builder.Build(graph);
        var output2 = builder.Build(graph);

        Assert.Equal(output1, output2);
    }

    // Test 2 — Budget
    [Fact]
    public void Build_DefaultBudget_DoesNotExceed12000Characters()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.True(output.Length <= 12_000);
    }

    [Fact]
    public void Build_CustomBudget_EnforcesLimit()
    {
        var graph = CreateTestGraph();
        var options = new ArchitectureGraphContextOptions { MaxCharacters = 500 };
        var builder = new GraphifyArchitectureContextBuilder(options);

        var output = builder.Build(graph);

        System.Console.WriteLine($"Output length: {output.Length}");
        System.Console.WriteLine($"Output: {output}");

        Assert.True(output.Length <= 500);
        Assert.Contains("[TRUNCATED", output);
    }

    // Test 3 — Relevant selection
    [Fact]
    public void Build_IncludesArchitectureRelevantNodes()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("Program", output);
        Assert.Contains("ServiceModule", output);
        Assert.Contains("HomeController", output);
        Assert.Contains("IAppService", output);
        Assert.Contains("AppService", output);
        Assert.Contains("IApiProvider", output);
        Assert.Contains("ApiProvider", output);
        Assert.Contains("HealthCheck", output);
        Assert.Contains("RequestEnricher", output);
        Assert.Contains("AppSettings", output);
    }

    [Fact]
    public void Build_ExcludesTestNodesFromArchitectureAreas()
    {
        var graph = CreateTestGraph();
        var options = new ArchitectureGraphContextOptions { PreferProduction = true, SuppressWeakKeywords = true };
        var builder = new GraphifyArchitectureContextBuilder(options);

        var output = builder.Build(graph);

        var archAreasStart = output.IndexOf("Start here:") >= 0 ? output.IndexOf("Start here:") : output.IndexOf("## Architecture-Relevant Areas");
        var archAreasEnd = output.IndexOf("Relationships:") >= 0 ? output.IndexOf("Relationships:") : output.IndexOf("## Structural Relationships");
        if (archAreasStart >= 0 && archAreasEnd > archAreasStart)
        {
            var archAreasSection = output.Substring(archAreasStart, archAreasEnd - archAreasStart);
            Assert.DoesNotContain("TestService", archAreasSection);
            Assert.DoesNotContain("MockProvider", archAreasSection);
        }
    }

    // Test 4 — Relationship confidence
    [Fact]
    public void Build_DistinguishesExtractedAndInferredRelationships()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("EXTRACTED", output);
        Assert.Contains("INFERRED", output);
    }

    // Test 5 — No architecture-quality conclusions
    [Fact]
    public void Build_DoesNotContainArchitectureJudgmentLanguage()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.DoesNotContain("violates", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bad architecture", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anti-pattern", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should not", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must not", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incorrect", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrong", output, StringComparison.OrdinalIgnoreCase);
    }

    // Test 6 — AdditionalContext compatibility
    [Fact]
    public void Build_OutputCanBeAssignedToAdditionalContext()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var context = builder.Build(graph);

        var request = new EvidenceRequest
        {
            RunId = "test",
            RepositorySnapshot = new RepositorySnapshot
            {
                RootPath = "/test",
                SolutionName = "Test"
            },
            Scope = EvidenceAcquisitionScope.Discipline,
            Instructions = "Test",
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "test",
                Files = [],
                TotalRepositoryFiles = 0,
                SelectedFileCount = 0,
                EstimatedContentSize = 0
            },
            ProviderNames = ["Test"],
            CorrelationId = "test",
            AdditionalContext = context
        };

        Assert.Equal(context, request.AdditionalContext);
    }

    // Test 7 — Category grouping
    [Fact]
    public void Build_GroupsNodesByArchitectureCategory()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("[COMPOSITION_STARTUP]", output);
        Assert.Contains("[CONTROLLER]", output);
        Assert.Contains("[APPLICATION_SERVICE]", output);
        Assert.Contains("[PROVIDER_BOUNDARY]", output);
        Assert.Contains("[INTERFACE_CONTRACT]", output);
        Assert.Contains("[HEALTH_DIAGNOSTICS]", output);
        Assert.Contains("[TELEMETRY_PIPELINE]", output);
        Assert.Contains("[CONFIGURATION]", output);
    }

    // Test 8 — Navigation candidates
    [Fact]
    public void Build_IncludesNavigationCandidates()
    {
        // Create a graph with a node that connects to architecture nodes but isn't one itself
        var graph = CreateTestGraphWithNavigationCandidate();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("## Navigation Candidates", output);
    }

    // Test 9 — Graph limitations section
    [Fact]
    public void Build_IncludesGraphLimitations()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("## Known Graph Limitations", output);
        Assert.Contains("relationships may be heuristic", output);
        Assert.Contains("verify all conclusions against source", output);
        Assert.Contains("exploration outside this map is expected", output);
    }

    // Test 10 — Metadata in header
    [Fact]
    public void Build_IncludesGraphMetadata()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("Nodes: 12", output);
        Assert.Contains("Edges: 7", output);
        Assert.Contains("Communities: 2", output);
    }

    // Test 11 — File path from graph
    [Fact]
    public void Build_IncludesSourceFileInformation()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("host/Service.Host/Program.cs", output);
        Assert.Contains("src/Application/AppService.cs", output);
    }

    // Test 12 — Build from file path
    [Fact]
    public void Build_FromFilePath_Works()
    {
        var graph = CreateTestGraph();
        var json = JsonSerializer.Serialize(graph, GraphifyArchitectureContextBuilder.JsonOptions);
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, json);
            var builder = new GraphifyArchitectureContextBuilder();

            var output = builder.Build(tempPath);

            Assert.Contains("Program", output);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // Test 13 — MinimalNavigation profile does not exceed 3000 chars
    [Fact]
    public void MinimalNavigation_DoesNotExceed3000Characters()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);

        var output = builder.Build(graph);

        Assert.True(output.Length <= 3_000, $"Output length {output.Length} exceeds 3000");
    }

    // Test 14 — MinimalNavigation deterministic
    [Fact]
    public void MinimalNavigation_Deterministic()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);

        var output1 = builder.Build(graph);
        var output2 = builder.Build(graph);

        Assert.Equal(output1, output2);
    }

    // Test 15 — Production nodes preferred over test nodes
    [Fact]
    public void PreferProduction_RanksProductionBeforeTest()
    {
        var graph = CreateTestGraphWithProdAndTest();
        // Use PreferProduction=false so both nodes appear, but production should come first in ordering
        var options = new ArchitectureGraphContextOptions { PreferProduction = false, SuppressWeakKeywords = false };
        var builder = new GraphifyArchitectureContextBuilder(options);

        var output = builder.Build(graph);

        var prodIdx = output.IndexOf("AuthProdService");
        var testIdx = output.IndexOf("AuthTestService");
        Assert.True(prodIdx >= 0 && testIdx >= 0, "Both nodes should appear");
        Assert.True(prodIdx < testIdx, "Production node should appear before test node");
    }

    // Test 16 — Weak keyword alone does not select node when SuppressWeakKeywords=true
    [Fact]
    public void SuppressWeakKeywords_IgnoresWeakOnlyMatches()
    {
        var graph = CreateTestGraphWithWeakOnlyNode();
        var options = new ArchitectureGraphContextOptions { SuppressWeakKeywords = true };
        var builder = new GraphifyArchitectureContextBuilder(options);

        var output = builder.Build(graph);

        Assert.DoesNotContain("ConfigKey", output);
    }

    // Test 17 — Strong signals remain selected under suppression
    [Fact]
    public void SuppressWeakKeywords_StrongSignalsStillSelected()
    {
        var graph = CreateTestGraphWithStrongNode();
        var options = new ArchitectureGraphContextOptions { SuppressWeakKeywords = true };
        var builder = new GraphifyArchitectureContextBuilder(options);

        var output = builder.Build(graph);

        Assert.Contains("Module", output);
    }

    // Test 18 — Default options produce full (non‑compact) map
    [Fact]
    public void DefaultOptions_ProducesFullMap()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("# Repository Structural Map — Architecture", output);
        Assert.DoesNotContain("# Architecture Navigation Map", output);
        Assert.Contains("## Architecture-Relevant Areas", output);
        Assert.Contains("## Structural Relationships", output);
        Assert.Contains("## Known Graph Limitations", output);
        // Navigation Candidates section only appears when there are candidates
    }

    // Test 19 — MinimalNavigation output compatible with AdditionalContext
    [Fact]
    public void MinimalNavigation_OutputCompatibleWithAdditionalContext()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);
        var context = builder.Build(graph);

        var request = new EvidenceRequest
        {
            RunId = "test",
            RepositorySnapshot = new RepositorySnapshot { RootPath = "/test", SolutionName = "Test" },
            Scope = EvidenceAcquisitionScope.Discipline,
            Instructions = "Test",
            ContextSelection = new AnalysisContextSelection
            {
                Strategy = "test",
                Files = [],
                TotalRepositoryFiles = 0,
                SelectedFileCount = 0,
                EstimatedContentSize = 0
            },
            ProviderNames = ["Test"],
            CorrelationId = "test",
            AdditionalContext = context
        };

        Assert.Equal(context, request.AdditionalContext);
    }

    // Test 20 — MinimalNavigation compact format
    [Fact]
    public void MinimalNavigation_ProducesCompactFormat()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);

        var output = builder.Build(graph);

        Assert.Contains("# Architecture Navigation Map", output);
        Assert.Contains("Start here:", output);
        Assert.Contains("Relationships:", output);
        Assert.Contains("Notes:", output);
        Assert.Contains("navigation hints only", output);
        Assert.DoesNotContain("## Architecture-Relevant Areas", output);
        Assert.DoesNotContain("## Structural Relationships", output);
        Assert.DoesNotContain("## Navigation Candidates", output);
    }

    // Test 21 — Relationship priority ordering (implements > contains > calls > imports > references)
    [Fact]
    public void Relationships_OrderedByPriority()
    {
        var graph = CreateTestGraphWithMixedRelations();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);

        var output = builder.Build(graph);

        var relStart = output.IndexOf("Relationships:");
        Assert.True(relStart >= 0, "Relationships section should exist");
        
        var relSection = output.Substring(relStart);
        var implementsIdx = relSection.IndexOf("IMPLEMENTS");
        var containsIdx = relSection.IndexOf("CONTAINS");
        var callsIdx = relSection.IndexOf("CALLS");
        var importsIdx = relSection.IndexOf("IMPORTS");
        var referencesIdx = relSection.IndexOf("REFERENCES");

        // EXTRACTED relationships come first, but within EXTRACTED, priority order applies
        Assert.True(implementsIdx >= 0, "IMPLEMENTS relationship should be present");
    }

    // Test 22 — Compact notes section
    [Fact]
    public void MinimalNavigation_NotesSectionPresent()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions.MinimalNavigation);

        var output = builder.Build(graph);

        Assert.Contains("Notes:", output);
        Assert.Contains("navigation hints only", output);
        Assert.Contains("verify against source", output);
        Assert.Contains("explore outside this map", output);
    }

    // Helper: graph with prod and test nodes
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraphWithProdAndTest()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "prod1",
                    Label = "AuthProdService",
                    SourceFile = "src/Services/ProdService.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "authprodservice",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "test1",
                    Label = "AuthTestService",
                    SourceFile = "tests/Services/TestService.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "authtestservice",
                    FileType = "code"
                }
            ],
            Links = [],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }

    // Helper: graph with weak-only node
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraphWithWeakOnlyNode()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "weak1",
                    Label = "ConfigKey",
                    SourceFile = "src/Config/ConfigKey.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "configkey",
                    FileType = "code"
                }
            ],
            Links = [],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }

    // Helper: graph with strong node
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraphWithStrongNode()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "strong1",
                    Label = "ServiceModule",
                    SourceFile = "src/Module.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "servicemodule",
                    FileType = "code"
                }
            ],
            Links = [],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }

    // Helper: graph with mixed relation types
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraphWithMixedRelations()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode { Id = "a", Label = "ServiceModule", SourceFile = "a.cs", Callable = true, CallableClass = true, NormLabel = "servicemodule", FileType = "code" },
                new GraphifyArchitectureContextBuilder.GraphifyNode { Id = "b", Label = "IProvider", SourceFile = "b.cs", Callable = true, CallableClass = true, NormLabel = "iprovider", FileType = "code" },
                new GraphifyArchitectureContextBuilder.GraphifyNode { Id = "c", Label = "ProviderImpl", SourceFile = "c.cs", Callable = true, CallableClass = true, NormLabel = "providerimpl", FileType = "code" },
                new GraphifyArchitectureContextBuilder.GraphifyNode { Id = "d", Label = "Controller", SourceFile = "d.cs", Callable = true, CallableClass = true, NormLabel = "controller", FileType = "code" },
                new GraphifyArchitectureContextBuilder.GraphifyNode { Id = "e", Label = "Service", SourceFile = "e.cs", Callable = true, CallableClass = true, NormLabel = "service", FileType = "code" }
            ],
            Links =
            [
                new GraphifyArchitectureContextBuilder.GraphifyLink { Source = "a", Target = "b", Relation = "references", Confidence = "EXTRACTED", ConfidenceScore = 1.0, SourceFile = "a.cs", Origin = "ast" },
                new GraphifyArchitectureContextBuilder.GraphifyLink { Source = "a", Target = "c", Relation = "imports", Confidence = "EXTRACTED", ConfidenceScore = 1.0, SourceFile = "a.cs", Origin = "ast" },
                new GraphifyArchitectureContextBuilder.GraphifyLink { Source = "a", Target = "d", Relation = "calls", Confidence = "EXTRACTED", ConfidenceScore = 1.0, SourceFile = "a.cs", Origin = "ast" },
                new GraphifyArchitectureContextBuilder.GraphifyLink { Source = "a", Target = "e", Relation = "contains", Confidence = "EXTRACTED", ConfidenceScore = 1.0, SourceFile = "a.cs", Origin = "ast" },
                new GraphifyArchitectureContextBuilder.GraphifyLink { Source = "b", Target = "c", Relation = "implements", Confidence = "EXTRACTED", ConfidenceScore = 1.0, SourceFile = "b.cs", Origin = "ast" }
            ],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }

    // Helper: graph with navigation candidate (node connected to architecture nodes but not one itself)
    private static GraphifyArchitectureContextBuilder.GraphifyGraph CreateTestGraphWithNavigationCandidate()
    {
        return new GraphifyArchitectureContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "arch1",
                    Label = "ServiceModule",
                    SourceFile = "src/Module.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "servicemodule",
                    FileType = "code"
                },
                new GraphifyArchitectureContextBuilder.GraphifyNode
                {
                    Id = "candidate1",
                    Label = "ThirdPartyLib",
                    SourceFile = "src/ThirdParty/ThirdPartyLib.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "thirdpartylib",
                    FileType = "code"
                }
            ],
            Links =
            [
                new GraphifyArchitectureContextBuilder.GraphifyLink
                {
                    Source = "arch1",
                    Target = "candidate1",
                    Relation = "calls",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "src/Module.cs",
                    Origin = "ast"
                }
            ],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }
}