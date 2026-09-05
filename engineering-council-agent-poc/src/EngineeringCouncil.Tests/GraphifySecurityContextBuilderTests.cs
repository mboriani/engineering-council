using System.IO;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Experiments;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class GraphifySecurityContextBuilderTests
{
    private static GraphifySecurityContextBuilder.GraphifyGraph CreateTestGraph()
    {
        return new GraphifySecurityContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node1",
                    Label = "AuthenticationHandler",
                    SourceFile = "src/Auth/AuthenticationHandler.cs",
                    SourceLocation = "L10",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "authenticationhandler",
                    FileType = "code"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node2",
                    Label = "TokenValidator",
                    SourceFile = "src/Auth/TokenValidator.cs",
                    SourceLocation = "L5",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "tokenvalidator",
                    FileType = "code"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node3",
                    Label = "AppSettings",
                    SourceFile = "src/appsettings.json",
                    SourceLocation = "L1",
                    NormLabel = "appsettings",
                    FileType = "config"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node4",
                    Label = "CustomSwaggerFilter",
                    SourceFile = "src/Filters/CustomSwaggerFilter.cs",
                    SourceLocation = "L8",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "customswaggerfilter",
                    FileType = "code"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node5",
                    Label = "UserService",
                    SourceFile = "src/Services/UserService.cs",
                    SourceLocation = "L20",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "userservice",
                    FileType = "code"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "node6",
                    Label = "EncryptionKey",
                    SourceFile = "src/Crypto/EncryptionKey.cs",
                    SourceLocation = "L15",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "encryptionkey",
                    FileType = "code"
                }
            ],
            Links =
            [
                new GraphifySecurityContextBuilder.GraphifyLink
                {
                    Source = "node1",
                    Target = "node2",
                    Relation = "calls",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "src/Auth/AuthenticationHandler.cs",
                    Origin = "ast"
                },
                new GraphifySecurityContextBuilder.GraphifyLink
                {
                    Source = "node2",
                    Target = "node6",
                    Relation = "references",
                    Confidence = "INFERRED",
                    ConfidenceScore = 0.85,
                    SourceFile = "src/Auth/TokenValidator.cs",
                    Origin = "ast"
                },
                new GraphifySecurityContextBuilder.GraphifyLink
                {
                    Source = "node5",
                    Target = "node1",
                    Relation = "calls",
                    Confidence = "EXTRACTED",
                    ConfidenceScore = 1.0,
                    SourceFile = "src/Services/UserService.cs",
                    Origin = "ast"
                },
                new GraphifySecurityContextBuilder.GraphifyLink
                {
                    Source = "node3",
                    Target = "node4",
                    Relation = "references",
                    Confidence = "INFERRED",
                    ConfidenceScore = 0.75,
                    SourceFile = "src/appsettings.json",
                    Origin = "ast"
                }
            ],
            Communities =
            [
                new GraphifySecurityContextBuilder.GraphifyCommunity
                {
                    Name = "Auth",
                    Cohesion = 0.8,
                    Nodes = ["node1", "node2"]
                },
                new GraphifySecurityContextBuilder.GraphifyCommunity
                {
                    Name = "Config",
                    Cohesion = 0.5,
                    Nodes = ["node3", "node4"]
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
        var builder = new GraphifySecurityContextBuilder();

        var output1 = builder.Build(graph);
        var output2 = builder.Build(graph);

        Assert.Equal(output1, output2);
    }

    // Test 2 — Budget
    [Fact]
    public void Build_DefaultBudget_DoesNotExceed12000Characters()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.True(output.Length <= 12_000);
    }

    [Fact]
    public void Build_CustomBudget_EnforcesLimit()
    {
        var graph = CreateTestGraph();
        var options = new GraphContextOptions { MaxCharacters = 500 };
        var builder = new GraphifySecurityContextBuilder(options);

        var output = builder.Build(graph);

        System.Console.WriteLine($"Output length: {output.Length}");
        System.Console.WriteLine($"Output: {output}");

        Assert.True(output.Length <= 500);
        Assert.Contains("[TRUNCATED", output);
    }

    // Test 3 — Relevant selection
    [Fact]
    public void Build_IncludesSecurityRelevantNodes()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("AuthenticationHandler", output);
        Assert.Contains("TokenValidator", output);
        Assert.Contains("AppSettings", output);
        Assert.Contains("CustomSwaggerFilter", output);
        Assert.Contains("EncryptionKey", output);
    }

    [Fact]
    public void Build_ExcludesIrrelevantNodesFromSecurityAreas()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        // UserService should not appear in the Security-Relevant Areas section
        // (it may appear in relationships/navigation as a connected node)
        var securityAreasStart = output.IndexOf("## Security-Relevant Areas");
        var securityAreasEnd = output.IndexOf("## Structural Relationships");
        if (securityAreasStart >= 0 && securityAreasEnd > securityAreasStart)
        {
            var securityAreasSection = output.Substring(securityAreasStart, securityAreasEnd - securityAreasStart);
            Assert.DoesNotContain("UserService", securityAreasSection);
        }
    }

    // Test 4 — Relationship confidence
    [Fact]
    public void Build_DistinguishesExtractedAndInferredRelationships()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("EXTRACTED", output);
        Assert.Contains("INFERRED", output);
    }

    // Test 5 — No vulnerability claims
    [Fact]
    public void Build_DoesNotContainVulnerabilityLanguage()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.DoesNotContain("vulnerability", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exposed secret", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insecure", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("critical finding", output, StringComparison.OrdinalIgnoreCase);
    }

    // Test 6 — AdditionalContext compatibility
    [Fact]
    public void Build_OutputCanBeAssignedToAdditionalContext()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

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
    public void Build_GroupsNodesBySecurityCategory()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("[AUTHENTICATION]", output);
        Assert.Contains("[CRYPTO_SECRETS]", output);
        Assert.Contains("[SWAGGER]", output);
        Assert.Contains("[CONFIGURATION]", output);
    }

    // Test 8 — Navigation candidates
    [Fact]
    public void Build_IncludesNavigationCandidates()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("## Navigation Candidates", output);
    }

    // Test 9 — Graph limitations section
    [Fact]
    public void Build_IncludesGraphLimitations()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

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
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("Nodes: 6", output);
        Assert.Contains("Edges: 4", output);
        Assert.Contains("Communities: 2", output);
    }

    // Test 11 — File path from graph
    [Fact]
    public void Build_IncludesSourceFileInformation()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder();

        var output = builder.Build(graph);

        Assert.Contains("src/Auth/AuthenticationHandler.cs", output);
        Assert.Contains("src/Auth/TokenValidator.cs", output);
    }

    // Test 12 — Build from file path
    [Fact]
    public void Build_FromFilePath_Works()
    {
        var graph = CreateTestGraph();
        var json = JsonSerializer.Serialize(graph, GraphifySecurityContextBuilder.JsonOptions);
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, json);
            var builder = new GraphifySecurityContextBuilder();

            var output = builder.Build(tempPath);

            Assert.Contains("AuthenticationHandler", output);
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
        var builder = new GraphifySecurityContextBuilder(GraphContextOptions.MinimalNavigation);

        var output = builder.Build(graph);

        Assert.True(output.Length <= 3_000, $"Output length {output.Length} exceeds 3000");
    }

    // Test 14 — MinimalNavigation deterministic
    [Fact]
    public void MinimalNavigation_Deterministic()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder(GraphContextOptions.MinimalNavigation);

        var output1 = builder.Build(graph);
        var output2 = builder.Build(graph);

        Assert.Equal(output1, output2);
    }

    // Test 15 — Production nodes preferred over test nodes
    [Fact]
    public void PreferProduction_RanksProductionBeforeTest()
    {
        var graph = CreateTestGraphWithTestFile();
        var options = new GraphContextOptions { PreferProduction = true, SuppressWeakKeywords = true };
        var builder = new GraphifySecurityContextBuilder(options);

        var output = builder.Build(graph);

        // Find positions of production and test nodes in the output
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
        var options = new GraphContextOptions { SuppressWeakKeywords = true };
        var builder = new GraphifySecurityContextBuilder(options);

        var output = builder.Build(graph);

        // Weak-only node "ConfigKey" should not appear in security areas
        Assert.DoesNotContain("ConfigKey", output);
    }

    // Test 17 — Strong signals remain selected under suppression
    [Fact]
    public void SuppressWeakKeywords_StrongSignalsStillSelected()
    {
        var graph = CreateTestGraphWithStrongNode();
        var options = new GraphContextOptions { SuppressWeakKeywords = true };
        var builder = new GraphifySecurityContextBuilder(options);

        var output = builder.Build(graph);

        Assert.Contains("AuthHandler", output);
    }

    // Test 18 — Default options produce full (non‑compact) map
    [Fact]
    public void DefaultOptions_ProducesFullMap()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder(); // default options

        var output = builder.Build(graph);

        // Full map header should be present, not compact header
        Assert.Contains("# Repository Structural Map — Security", output);
        Assert.DoesNotContain("# Security Navigation Map", output);
        Assert.Contains("## Security-Relevant Areas", output);
        Assert.Contains("## Structural Relationships", output);
        Assert.Contains("## Navigation Candidates", output);
        Assert.Contains("## Known Graph Limitations", output);
    }

    // Test 19 — MinimalNavigation output compatible with AdditionalContext
    [Fact]
    public void MinimalNavigation_OutputCompatibleWithAdditionalContext()
    {
        var graph = CreateTestGraph();
        var builder = new GraphifySecurityContextBuilder(GraphContextOptions.MinimalNavigation);
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

    // Helper: graph with a test‑file node (both have strong auth keyword)
    private static GraphifySecurityContextBuilder.GraphifyGraph CreateTestGraphWithTestFile()
    {
        return new GraphifySecurityContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "prod1",
                    Label = "AuthProdService",
                    SourceFile = "src/Services/ProdService.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "authprodservice",
                    FileType = "code"
                },
                new GraphifySecurityContextBuilder.GraphifyNode
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

    // Helper: graph with weak‑only node
    private static GraphifySecurityContextBuilder.GraphifyGraph CreateTestGraphWithWeakOnlyNode()
    {
        return new GraphifySecurityContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifySecurityContextBuilder.GraphifyNode
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
    private static GraphifySecurityContextBuilder.GraphifyGraph CreateTestGraphWithStrongNode()
    {
        return new GraphifySecurityContextBuilder.GraphifyGraph
        {
            Nodes =
            [
                new GraphifySecurityContextBuilder.GraphifyNode
                {
                    Id = "strong1",
                    Label = "AuthHandler",
                    SourceFile = "src/Auth/AuthHandler.cs",
                    Callable = true,
                    CallableClass = true,
                    NormLabel = "authhandler",
                    FileType = "code"
                }
            ],
            Links = [],
            Communities = [],
            BuiltAtCommit = "abc123"
        };
    }
}