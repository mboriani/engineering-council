using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineeringCouncil.Infrastructure.Experiments;

/// <summary>
/// Builds a deterministic, security-oriented textual context from Graphify's
/// repository graph output. The result is suitable for
/// <see cref="Core.Abstractions.EvidenceRequest.AdditionalContext"/>.
/// </summary>
public sealed class GraphifySecurityContextBuilder
{
    private readonly GraphContextOptions _options;

    public GraphifySecurityContextBuilder(GraphContextOptions? options = null)
    {
        _options = options ?? new GraphContextOptions();
    }

    /// <summary>
    /// Builds the security context from a Graphify graph.json file.
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
    /// Builds the security context from an already-parsed Graphify graph.
    /// </summary>
    /// <param name="graph">The parsed Graphify graph.</param>
    /// <returns>A compact textual context for agentic evidence acquisition.</returns>
    public string Build(GraphifyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var context = new StringBuilder();

        // Determine compact mode
        bool compact = _options.MaxCharacters <= 3000;

        if (compact)
        {
            context.AppendLine("# Security Navigation Map");
            context.AppendLine();
        }
        else
        {
            // Header
            context.AppendLine("# Repository Structural Map — Security");
            context.AppendLine();
            context.AppendLine("Graph:");
            context.AppendLine($"- Nodes: {graph.Nodes?.Count ?? 0}");
            context.AppendLine($"- Edges: {graph.Links?.Count ?? 0}");
            context.AppendLine($"- Communities: {graph.Communities?.Count ?? 0}");
            context.AppendLine("- Extraction: deterministic Graphify analysis");
            context.AppendLine();
        }

        // Security-relevant areas
        var securityNodes = SelectSecurityRelevantNodes(graph);
        // Limit total nodes across categories
        if (securityNodes.Count > _options.MaxTotalSecurityNodes)
            securityNodes = securityNodes.Take(_options.MaxTotalSecurityNodes).ToList();

        if (securityNodes.Count > 0)
        {
            if (compact)
            {
                context.AppendLine("Start here:");
                context.AppendLine();
                int idx = 1;
                var grouped = GroupByCategory(securityNodes);
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
                context.AppendLine("## Security-Relevant Areas");
                context.AppendLine();

                var grouped = GroupByCategory(securityNodes);
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

        // Structural relationships
        var securityLinks = SelectSecurityRelevantLinks(graph, securityNodes);
        if (securityLinks.Count > 0)
        {
            if (compact)
            {
                context.AppendLine("Relationships:");
                foreach (var link in securityLinks.Take(_options.MaxRelationships))
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

                foreach (var link in securityLinks.Take(_options.MaxRelationships))
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

        // Navigation candidates
        var navigationNodes = SelectNavigationCandidates(graph, securityNodes);
        if (navigationNodes.Count > 0)
        {
            if (compact)
            {
                // In compact mode, navigation candidates already listed in Start here, skip separate section
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

        // Graph limitations / notes
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

    private List<GraphifyNode> SelectSecurityRelevantNodes(GraphifyGraph graph)
    {
        if (graph.Nodes is null)
            return [];

        var strongKeywords = new[]
        {
            "auth", "authentication", "authorization", "token", "credential", "secret",
            "encrypt", "crypto", "password", "certificate", "cors", "swagger",
            "identity", "session", "jwt", "oauth", "openid", "claims", "principal",
            "secure", "sign", "verify", "hash", "tls", "ssl", "https", "csrf", "xss"
        };

        var weakKeywords = new[]
        {
            "key", "header", "cookie", "configuration", "config", "appsettings", "request filter"
        };

        var selected = new List<GraphifyNode>();

        foreach (var node in graph.Nodes)
        {
            if (node.Label is null)
                continue;

            var labelLower = node.Label.ToLowerInvariant();
            var idLower = node.Id.ToLowerInvariant();
            var fileLower = (node.SourceFile ?? string.Empty).ToLowerInvariant();

            bool strongMatch = strongKeywords.Any(kw =>
                labelLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                idLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                fileLower.Contains(kw, StringComparison.OrdinalIgnoreCase));

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

            if (isRelevant)
                selected.Add(node);
        }

        // Production preference
        if (_options.PreferProduction)
        {
            selected = selected
                .OrderBy(n => IsTestFile(n.SourceFile) ? 1 : 0)
                .ThenBy(n => GetCategoryPriority(GetCategory(n)))
                .ThenBy(n => n.Label, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            selected = selected
                .OrderBy(n => GetCategoryPriority(GetCategory(n)))
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
        return lower.Contains("/test/") || lower.Contains("/tests/") || lower.Contains("\\test\\") || lower.Contains("\\tests\\") || lower.EndsWith("tests.cs") || lower.Contains("test");
    }

    private List<GraphifyLink> SelectSecurityRelevantLinks(GraphifyGraph graph, List<GraphifyNode> securityNodes)
    {
        if (graph.Links is null || securityNodes.Count == 0)
            return [];

        var securityNodeIds = new HashSet<string>(securityNodes.Select(n => n.Id));
        var relevantLinks = new List<GraphifyLink>();

        foreach (var link in graph.Links)
        {
            var sourceInSelection = securityNodeIds.Contains(link.Source);
            var targetInSelection = securityNodeIds.Contains(link.Target);

            if (sourceInSelection || targetInSelection)
            {
                relevantLinks.Add(link);
            }
        }

        // Order: EXTRACTED first, then by confidence score descending, then deterministic
        return relevantLinks
            .OrderByDescending(l => l.Confidence == "EXTRACTED" ? 1 : 0)
            .ThenByDescending(l => l.ConfidenceScore ?? 0)
            .ThenBy(l => l.Source, StringComparer.Ordinal)
            .ThenBy(l => l.Target, StringComparer.Ordinal)
            .ToList();
    }

    private List<GraphifyNode> SelectNavigationCandidates(GraphifyGraph graph, List<GraphifyNode> securityNodes)
    {
        if (graph.Nodes is null || graph.Links is null || securityNodes.Count == 0)
            return [];

        var securityNodeIds = new HashSet<string>(securityNodes.Select(n => n.Id));
        var candidateScores = new Dictionary<string, int>();

        foreach (var link in graph.Links)
        {
            var sourceInSelection = securityNodeIds.Contains(link.Source);
            var targetInSelection = securityNodeIds.Contains(link.Target);

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
            .OrderByDescending(n => candidateScores[n.Id])
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

        // Order categories by priority
        return groups
            .OrderBy(kvp => GetCategoryPriority(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private string GetCategory(GraphifyNode node)
    {
        var labelLower = (node.Label ?? string.Empty).ToLowerInvariant();
        var fileLower = (node.SourceFile ?? string.Empty).ToLowerInvariant();

        if (labelLower.Contains("auth") || labelLower.Contains("token") || labelLower.Contains("credential") ||
            labelLower.Contains("jwt") || labelLower.Contains("oauth") || labelLower.Contains("openid") ||
            labelLower.Contains("claims") || labelLower.Contains("principal") || labelLower.Contains("identity"))
            return "AUTHENTICATION";

        if (labelLower.Contains("swagger") || fileLower.Contains("swagger"))
            return "SWAGGER";

        if (labelLower.Contains("cors") || labelLower.Contains("cookie") || labelLower.Contains("session") ||
            labelLower.Contains("csrf") || labelLower.Contains("header"))
            return "CORS_HEADERS";

        if (labelLower.Contains("encrypt") || labelLower.Contains("crypto") || labelLower.Contains("password") ||
            labelLower.Contains("secret") || labelLower.Contains("certificate") ||
            labelLower.Contains("sign") || labelLower.Contains("verify") || labelLower.Contains("hash") ||
            labelLower.Contains("tls") || labelLower.Contains("ssl"))
            return "CRYPTO_SECRETS";

        if (labelLower.Contains("config") || labelLower.Contains("appsettings") || fileLower.Contains("appsettings"))
            return "CONFIGURATION";

        if (labelLower.Contains("request") && (labelLower.Contains("filter") || labelLower.Contains("enrich")))
            return "REQUEST_FILTER";

        return "OTHER_SECURITY";
    }

    private int GetCategoryPriority(string category) => category switch
    {
        "AUTHENTICATION" => 0,
        "CRYPTO_SECRETS" => 1,
        "SWAGGER" => 2,
        "CORS_HEADERS" => 3,
        "REQUEST_FILTER" => 4,
        "CONFIGURATION" => 5,
        _ => 6
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

        // Reserve space for truncation notice
        const string truncationNotice = "\n[TRUNCATED: budget exceeded]";
        var availableLength = _options.MaxCharacters - truncationNotice.Length;
        if (availableLength < 100)
            availableLength = 100; // Minimum viable context

        // Truncate at the last complete line before budget
        var truncated = text.Substring(0, availableLength);
        var lastNewline = truncated.LastIndexOf('\n');
        if (lastNewline > availableLength * 0.8) // Only truncate at newline if reasonably close
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
/// Options for controlling Graphify security context generation.
/// </summary>
public sealed class GraphContextOptions
{
    /// <summary>Maximum characters in the generated context. Default 12,000 (full map).</summary>
    public int MaxCharacters { get; init; } = 12_000;

    /// <summary>Maximum nodes per security category. Default 10.</summary>
    public int MaxNodesPerCategory { get; init; } = 10;

    /// <summary>Maximum structural relationships to include. Default 20.</summary>
    public int MaxRelationships { get; init; } = 20;

    /// <summary>Maximum navigation candidates to include. Default 10.</summary>
    public int MaxNavigationCandidates { get; init; } = 10;

    /// <summary>Maximum total security nodes across all categories. Default 100 (effectively unlimited).</summary>
    public int MaxTotalSecurityNodes { get; init; } = 100;

    /// <summary>Prefer production code over test code when selecting nodes. Default false (original behavior).</summary>
    public bool PreferProduction { get; init; } = false;

    /// <summary>Suppress weak keywords (e.g., "key", "header", "configuration") unless accompanied by a strong security keyword. Default false (original behavior).</summary>
    public bool SuppressWeakKeywords { get; init; } = false;

    /// <summary>Pre-configured minimal navigation profile (≈3 000 chars).</summary>
    public static GraphContextOptions MinimalNavigation => new()
    {
        MaxCharacters = 3_000,
        MaxNodesPerCategory = 5,
        MaxRelationships = 10,
        MaxNavigationCandidates = 5,
        MaxTotalSecurityNodes = 15,
        PreferProduction = true,
        SuppressWeakKeywords = true
    };
}