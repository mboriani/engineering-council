using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineeringCouncil.Infrastructure.Experiments;

/// <summary>
/// Builds a deterministic, architecture-oriented textual context from Graphify's
/// repository graph output. The result is suitable for
/// <see cref="Core.Abstractions.EvidenceRequest.AdditionalContext"/>.
/// </summary>
public sealed class GraphifyArchitectureContextBuilder
{
    private readonly ArchitectureGraphContextOptions _options;

    public GraphifyArchitectureContextBuilder(ArchitectureGraphContextOptions? options = null)
    {
        _options = options ?? new ArchitectureGraphContextOptions();
    }

    /// <summary>
    /// Builds the architecture context from a Graphify graph.json file.
    /// </summary>
    /// <param name="graphJsonPath">Path to the Graphify graph.json file.</param>
    /// <returns>A compact textual context for agentic evidence acquisition.</returns>
    public string Build(string graphJsonPath)
    {
        var graphJson = File.ReadAllText(graphJsonPath);
        var graph = JsonSerializer.Deserialize<GraphifyGraph>(graphJson, JsonOptions);
        return Build(graph);
    }

    /// <summary>
    /// Builds the architecture context from an already-parsed Graphify graph.
    /// </summary>
    /// <param name="graph">The parsed Graphify graph.</param>
    /// <returns>A compact textual context for agentic evidence acquisition.</returns>
    public string Build(GraphifyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var context = new StringBuilder();

        bool compact = _options.MaxCharacters <= 3000;

        if (compact)
        {
            context.AppendLine("# Architecture Navigation Map");
            context.AppendLine();
        }
        else
        {
            context.AppendLine("# Repository Structural Map — Architecture");
            context.AppendLine();
            context.AppendLine("Graph:");
            context.AppendLine($"- Nodes: {graph.Nodes?.Count ?? 0}");
            context.AppendLine($"- Edges: {graph.Links?.Count ?? 0}");
            context.AppendLine($"- Communities: {graph.Communities?.Count ?? 0}");
            context.AppendLine("- Extraction: deterministic Graphify analysis");
            context.AppendLine();
        }

        var architectureNodes = SelectArchitectureRelevantNodes(graph);
        if (architectureNodes.Count > _options.MaxTotalArchitectureNodes)
            architectureNodes = architectureNodes.Take(_options.MaxTotalArchitectureNodes).ToList();

        if (architectureNodes.Count > 0)
        {
            if (compact)
            {
                context.AppendLine("Start here:");
                context.AppendLine();
                int idx = 1;
                var grouped = GroupByCategory(architectureNodes);
                foreach (var (category, nodes) in grouped)
                {
                    foreach (var node in nodes.Take(_options.MaxNodesPerCategory))
                    {
                        var file = node.SourceFile ?? "unknown";
                        var symbol = node.Label ?? "unknown";
                        context.AppendLine($"{idx}. {file}");
                        context.AppendLine($"   {symbol}");
                        context.AppendLine($"   {category.ToLowerInvariant()}-related");
                        context.AppendLine();
                        idx++;
                    }
                }
            }
            else
            {
                context.AppendLine("## Architecture-Relevant Areas");
                context.AppendLine();

                var grouped = GroupByCategory(architectureNodes);
                foreach (var (category, nodes) in grouped)
                {
                    context.AppendLine($"[{category}]");
                    foreach (var node in nodes.Take(_options.MaxNodesPerCategory))
                    {
                        context.AppendLine($"  - File: {node.SourceFile ?? "unknown"}");
                        context.AppendLine($"    Symbol: {node.Label}");
                        if (!string.IsNullOrWhiteSpace(node.SourceLocation))
                            context.AppendLine($"    Location: {node.SourceLocation}");
                        if (!string.IsNullOrWhiteSpace(node.NormLabel))
                            context.AppendLine($"    Type: {GetNodeType(node)}");
                    }
                    context.AppendLine();
                }
            }
        }

        var architectureLinks = SelectArchitectureRelevantLinks(graph, architectureNodes);
        if (architectureLinks.Count > 0)
        {
            if (compact)
            {
                context.AppendLine("Relationships:");
                foreach (var link in architectureLinks.Take(_options.MaxRelationships))
                {
                    var source = graph.Nodes?.FirstOrDefault(n => n.Id == link.Source);
                    var target = graph.Nodes?.FirstOrDefault(n => n.Id == link.Target);
                    if (source is not null && target is not null)
                    {
                        var rel = link.Relation?.ToUpperInvariant() ?? "RELATES_TO";
                        var conf = link.Confidence ?? "UNKNOWN";
                        context.AppendLine($"- {source.Label} → {rel} → {target.Label} [{conf}]");
                    }
                }
                context.AppendLine();
            }
            else
            {
                context.AppendLine("## Structural Relationships");
                context.AppendLine();

                foreach (var link in architectureLinks.Take(_options.MaxRelationships))
                {
                    var source = graph.Nodes?.FirstOrDefault(n => n.Id == link.Source);
                    var target = graph.Nodes?.FirstOrDefault(n => n.Id == link.Target);
                    if (source is not null && target is not null)
                    {
                        var confidenceLabel = link.Confidence ?? "UNKNOWN";
                        context.AppendLine($"{source.Label} → {link.Relation?.ToUpperInvariant() ?? "RELATES_TO"} → {target.Label}");
                        context.AppendLine($"  Confidence: {confidenceLabel}");
                        if (!string.IsNullOrWhiteSpace(link.SourceFile))
                            context.AppendLine($"  Source: {link.SourceFile}");
                        if (link.ConfidenceScore.HasValue)
                            context.AppendLine($"  Score: {link.ConfidenceScore.Value:0.00}");
                    }
                }
                context.AppendLine();
            }
        }

        var navigationNodes = SelectNavigationCandidates(graph, architectureNodes);
        if (navigationNodes.Count > 0)
        {
            if (compact)
            {
            }
            else
            {
                context.AppendLine("## Navigation Candidates");
                context.AppendLine();

                for (int i = 0; i < navigationNodes.Count && i < _options.MaxNavigationCandidates; i++)
                {
                    var node = navigationNodes[i];
                    context.AppendLine($"{i + 1}. {node.Label} ({node.SourceFile ?? "unknown"})");
                }
                context.AppendLine();
            }
        }

        if (compact)
        {
            context.AppendLine("Notes:");
            context.AppendLine("- navigation hints only");
            context.AppendLine("- verify against source");
            context.AppendLine("- explore outside this map");
        }
        else
        {
            context.AppendLine("## Known Graph Limitations");
            context.AppendLine("- relationships may be heuristic");
            context.AppendLine("- absence from this map does not imply absence from repository");
            context.AppendLine("- verify all conclusions against source");
            context.AppendLine("- exploration outside this map is expected");
        }

        var result = context.ToString();
        return EnforceBudget(result);
    }

    private List<GraphifyNode> SelectArchitectureRelevantNodes(GraphifyGraph graph)
    {
        if (graph.Nodes is null)
            return [];

        var strongKeywords = new[]
        {
            "module", "startup", "program", "bootstrap", "configure",
            "dependencyinjection", "serviceregistration", "servicecollection",
            "controller", "handler", "service", "provider", "repository",
            "interface", "adapter", "client", "gateway", "infrastructure",
            "application", "core", "domain", "facade", "mediator",
            "httpclient", "external", "api", "messaging", "dapr",
            "database", "repository", "configuration", "options", "settings",
            "healthcheck", "diagnostics", "telemetry", "enricher", "filter",
            "middleware", "pipeline", "interceptor", "decorator"
        };

        var weakKeywords = new[]
        {
            "builder", "factory", "extension", "helper", "util", "base",
            "abstract", "attribute", "model", "dto", "entity", "valueobject",
            "specification", "validator", "mapper", "converter"
        };

        var testPathPatterns = new[]
        {
            "/test/", "/tests/", "\\test\\", "\\tests\\",
            "test/", "tests/",  // paths starting with test/ or tests/
            "test.cs", "tests.cs", "test.", "tests.",
            "mock", "fake", "fixture", "integration", "unittest"
        };

        var selected = new List<GraphifyNode>();

        foreach (var node in graph.Nodes)
        {
            if (node.Label is null)
                continue;

            var labelLower = node.Label.ToLowerInvariant();
            var idLower = node.Id.ToLowerInvariant();
            var fileLower = (node.SourceFile ?? string.Empty).ToLowerInvariant();

            bool isTestFile = testPathPatterns.Any(p => fileLower.Contains(p));

            // Prefer class-level nodes (CallableClass == true) over method-level nodes
            bool isClassLevel = node.CallableClass == true;
            bool isMethodOnly = node.Callable == true && node.CallableClass != true;

            // Strong keywords that indicate architectural significance
            bool strongMatch = strongKeywords.Any(kw =>
                labelLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                idLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                fileLower.Contains(kw, StringComparison.OrdinalIgnoreCase));

            // Weak keywords that might indicate architectural relevance
            bool weakMatch = weakKeywords.Any(kw =>
                labelLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                idLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                fileLower.Contains(kw, StringComparison.OrdinalIgnoreCase));

            bool isRelevant;
            if (_options.SuppressWeakKeywords)
            {
                isRelevant = strongMatch;
            }
            else
            {
                isRelevant = strongMatch || weakMatch;
            }

            // Additional filter: for method-only nodes, require stronger architectural signal
            // (e.g., interface methods, not implementation details)
            if (isRelevant && isMethodOnly)
            {
                // Only include method-only nodes if they are interface methods or have specific architectural significance
                bool isInterfaceMethod = labelLower.Contains("interface") || idLower.Contains("interface");
                bool isArchitecturalMethod = labelLower.Contains("configure") && (labelLower.Contains("service") || labelLower.Contains("module") || labelLower.Contains("startup"));
                
                if (!isInterfaceMethod && !isArchitecturalMethod)
                {
                    isRelevant = false;
                }
            }

            if (isRelevant)
            {
                if (_options.PreferProduction && isTestFile)
                    continue;
                selected.Add(node);
            }
        }

        if (_options.PreferProduction)
        {
            selected = selected
                .OrderBy(n => IsTestFile(n.SourceFile) ? 1 : 0)
                .ThenBy(n => n.CallableClass == true ? 0 : 1)  // Prefer class-level nodes
                .ThenBy(n => GetCategoryPriority(GetCategory(n)))
                .ThenBy(n => n.Label, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            selected = selected
                .OrderBy(n => n.CallableClass == true ? 0 : 1)  // Prefer class-level nodes
                .ThenBy(n => GetCategoryPriority(GetCategory(n)))
                .ThenBy(n => n.Label, StringComparer.Ordinal)
                .ToList();
        }

        return selected;
    }

    private bool IsTestFile(string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            return false;
        var lower = sourceFile.ToLowerInvariant();
        return lower.Contains("/test/") || lower.Contains("/tests/") ||
               lower.StartsWith("test/") || lower.StartsWith("tests/") ||
               lower.Contains("\\test\\") || lower.Contains("\\tests\\") ||
               lower.EndsWith("tests.cs") || lower.Contains("test") ||
               lower.Contains("mock") || lower.Contains("fake") || lower.Contains("fixture");
    }

    private List<GraphifyLink> SelectArchitectureRelevantLinks(GraphifyGraph graph, List<GraphifyNode> architectureNodes)
    {
        if (graph.Links is null || architectureNodes.Count == 0)
            return [];

        var architectureNodeIds = new HashSet<string>(architectureNodes.Select(n => n.Id));
        var relevantLinks = new List<GraphifyLink>();

        var priorityRelations = new[]
        {
            "implements", "contains", "calls", "imports", "references",
            "inherits", "method", "property"
        };

        foreach (var link in graph.Links)
        {
            var sourceInSelection = architectureNodeIds.Contains(link.Source);
            var targetInSelection = architectureNodeIds.Contains(link.Target);

            if (sourceInSelection || targetInSelection)
            {
                relevantLinks.Add(link);
            }
        }

        return relevantLinks
            .OrderByDescending(l => l.Confidence == "EXTRACTED" ? 1 : 0)
            .ThenBy(l =>
            {
                var rel = (l.Relation ?? string.Empty).ToLowerInvariant();
                var idx = Array.IndexOf(priorityRelations, rel);
                return idx >= 0 ? idx : priorityRelations.Length;
            })
            .ThenByDescending(l => l.ConfidenceScore ?? 0)
            .ThenBy(l => l.Source, StringComparer.Ordinal)
            .ThenBy(l => l.Target, StringComparer.Ordinal)
            .ToList();
    }

    private List<GraphifyNode> SelectNavigationCandidates(GraphifyGraph graph, List<GraphifyNode> architectureNodes)
    {
        if (graph.Nodes is null || graph.Links is null || architectureNodes.Count == 0)
            return [];

        var architectureNodeIds = new HashSet<string>(architectureNodes.Select(n => n.Id));
        var candidateScores = new Dictionary<string, int>();

        foreach (var link in graph.Links)
        {
            var sourceInSelection = architectureNodeIds.Contains(link.Source);
            var targetInSelection = architectureNodeIds.Contains(link.Target);

            if (sourceInSelection && !targetInSelection)
            {
                candidateScores[link.Target] = candidateScores.GetValueOrDefault(link.Target, 0) + 1;
            }
            else if (targetInSelection && !sourceInSelection)
            {
                candidateScores[link.Source] = candidateScores.GetValueOrDefault(link.Source, 0) + 1;
            }
        }

        var candidates = graph.Nodes
            .Where(n => candidateScores.ContainsKey(n.Id))
            .Where(n => !architectureNodeIds.Contains(n.Id))
            .OrderByDescending(n => candidateScores[n.Id])
            .ThenBy(n => IsTestFile(n.SourceFile) ? 1 : 0)
            .ThenBy(n => n.Label, StringComparer.Ordinal)
            .ToList();

        return candidates;
    }

    private Dictionary<string, List<GraphifyNode>> GroupByCategory(List<GraphifyNode> nodes)
    {
        var groups = new Dictionary<string, List<GraphifyNode>>();

        foreach (var node in nodes)
        {
            var category = GetCategory(node);
            if (!groups.ContainsKey(category))
                groups[category] = new List<GraphifyNode>();
            groups[category].Add(node);
        }

        return groups
            .OrderBy(kvp => GetCategoryPriority(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private string GetCategory(GraphifyNode node)
    {
        var labelLower = (node.Label ?? string.Empty).ToLowerInvariant();
        var fileLower = (node.SourceFile ?? string.Empty).ToLowerInvariant();

        if (labelLower.Contains("module") || labelLower.Contains("startup") ||
            labelLower.Contains("program") || labelLower.Contains("bootstrap") ||
            labelLower.Contains("configure") || fileLower.Contains("program.cs") ||
            fileLower.Contains("module.cs"))
            return "COMPOSITION_STARTUP";

        if (labelLower.Contains("controller") || fileLower.Contains("/controllers/"))
            return "CONTROLLER";

        if (labelLower.Contains("interface") || labelLower.StartsWith("i") &&
            (labelLower.Contains("provider") || labelLower.Contains("service") ||
             labelLower.Contains("repository") || labelLower.Contains("handler") ||
             labelLower.Contains("client") || labelLower.Contains("gateway")))
            return "INTERFACE_CONTRACT";

        if (labelLower.Contains("service") && !labelLower.Contains("health") &&
            !labelLower.Contains("telemetry") && !labelLower.Contains("diagnostics"))
            return "APPLICATION_SERVICE";

        if (labelLower.Contains("provider") || labelLower.Contains("repository") ||
            labelLower.Contains("gateway") || labelLower.Contains("client") ||
            labelLower.Contains("adapter") || labelLower.Contains("handler"))
            return "PROVIDER_BOUNDARY";

        if (labelLower.Contains("healthcheck") || fileLower.Contains("/healthcheck") ||
            labelLower.Contains("diagnostics") || fileLower.Contains("/diagnostics/"))
            return "HEALTH_DIAGNOSTICS";

        if (labelLower.Contains("telemetry") || labelLower.Contains("enricher") ||
            labelLower.Contains("filter") || fileLower.Contains("/telemetry/") ||
            fileLower.Contains("/filters/"))
            return "TELEMETRY_PIPELINE";

        if (labelLower.Contains("configuration") || labelLower.Contains("options") ||
            labelLower.Contains("settings") || fileLower.Contains("/options/") ||
            fileLower.Contains("/settings/") || fileLower.Contains("appsettings"))
            return "CONFIGURATION";

        return "OTHER_ARCHITECTURE";
    }

    private int GetCategoryPriority(string category) => category switch
    {
        "COMPOSITION_STARTUP" => 0,
        "CONTROLLER" => 1,
        "APPLICATION_SERVICE" => 2,
        "PROVIDER_BOUNDARY" => 3,
        "INTERFACE_CONTRACT" => 4,
        "HEALTH_DIAGNOSTICS" => 5,
        "TELEMETRY_PIPELINE" => 6,
        "CONFIGURATION" => 7,
        _ => 8
    };

    private string GetNodeType(GraphifyNode node)
    {
        if (node.Callable == true && node.CallableClass == true)
            return "class";
        if (node.Callable == true)
            return "method";
        if (node.FileType == "code")
            return "type";
        return "node";
    }

    private string EnforceBudget(string text)
    {
        if (text.Length <= _options.MaxCharacters)
            return text;

        const string truncationNotice = "\n[TRUNCATED: budget exceeded]";
        var availableLength = _options.MaxCharacters - truncationNotice.Length;
        if (availableLength < 100)
            availableLength = 100;

        var truncated = text.Substring(0, availableLength);
        var lastNewline = truncated.LastIndexOf('\n');
        if (lastNewline > availableLength * 0.8)
            return truncated.Substring(0, lastNewline) + truncationNotice;

        return truncated + truncationNotice;
    }

    /// <summary>
    /// JSON serializer options used for Graphify graph deserialization.
    /// Public for test access.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Graphify graph.json structure.
    /// </summary>
    public sealed class GraphifyGraph
    {
        [JsonPropertyName("nodes")]
        public List<GraphifyNode>? Nodes { get; set; }

        [JsonPropertyName("links")]
        public List<GraphifyLink>? Links { get; set; }

        [JsonPropertyName("hyperedges")]
        public List<object>? Hyperedges { get; set; }

        [JsonPropertyName("communities")]
        public List<GraphifyCommunity>? Communities { get; set; }

        [JsonPropertyName("built_at_commit")]
        public string? BuiltAtCommit { get; set; }
    }

    public sealed class GraphifyNode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("_callable")]
        public bool? Callable { get; set; }

        [JsonPropertyName("_callable_class")]
        public bool? CallableClass { get; set; }

        [JsonPropertyName("_origin")]
        public string? Origin { get; set; }

        [JsonPropertyName("community")]
        public int? Community { get; set; }

        [JsonPropertyName("community_name")]
        public string? CommunityName { get; set; }

        [JsonPropertyName("file_type")]
        public string? FileType { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        [JsonPropertyName("norm_label")]
        public string? NormLabel { get; set; }

        [JsonPropertyName("source_file")]
        public string? SourceFile { get; set; }

        [JsonPropertyName("source_location")]
        public string? SourceLocation { get; set; }
    }

    public sealed class GraphifyLink
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        [JsonPropertyName("relation")]
        public string? Relation { get; set; }

        [JsonPropertyName("_origin")]
        public string? Origin { get; set; }

        [JsonPropertyName("confidence")]
        public string? Confidence { get; set; }

        [JsonPropertyName("confidence_score")]
        public double? ConfidenceScore { get; set; }

        [JsonPropertyName("context")]
        public string? Context { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        [JsonPropertyName("source_file")]
        public string? SourceFile { get; set; }

        [JsonPropertyName("source_location")]
        public string? SourceLocation { get; set; }

        [JsonPropertyName("weight")]
        public double? Weight { get; set; }
    }

    public sealed class GraphifyCommunity
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("cohesion")]
        public double? Cohesion { get; set; }

        [JsonPropertyName("nodes")]
        public List<string>? Nodes { get; set; }
    }
}

/// <summary>
/// Options for controlling Graphify architecture context generation.
/// </summary>
public sealed class ArchitectureGraphContextOptions
{
    /// <summary>Maximum characters in the generated context. Default 12,000 (full map).</summary>
    public int MaxCharacters { get; init; } = 12_000;

    /// <summary>Maximum nodes per architecture category. Default 10.</summary>
    public int MaxNodesPerCategory { get; init; } = 10;

    /// <summary>Maximum structural relationships to include. Default 20.</summary>
    public int MaxRelationships { get; init; } = 20;

    /// <summary>Maximum navigation candidates to include. Default 10.</summary>
    public int MaxNavigationCandidates { get; init; } = 10;

    /// <summary>Maximum total architecture nodes across all categories. Default 100 (effectively unlimited).</summary>
    public int MaxTotalArchitectureNodes { get; init; } = 100;

    /// <summary>Prefer production code over test code when selecting nodes. Default false (original behavior).</summary>
    public bool PreferProduction { get; init; } = false;

    /// <summary>Suppress weak keywords unless accompanied by a strong architecture keyword. Default false (original behavior).</summary>
    public bool SuppressWeakKeywords { get; init; } = false;

    /// <summary>Pre-configured minimal navigation profile (≈3 000 chars).</summary>
    public static ArchitectureGraphContextOptions MinimalNavigation => new()
    {
        MaxCharacters = 3_000,
        MaxNodesPerCategory = 5,
        MaxRelationships = 10,
        MaxNavigationCandidates = 5,
        MaxTotalArchitectureNodes = 15,
        PreferProduction = true,
        SuppressWeakKeywords = true
    };
}