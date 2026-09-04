using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Acquisition;

/// <summary>
/// Deterministic, rule-based context selector. It ranks the already-scanned files
/// (so scanner ignore rules + content limits are respected) by discipline-specific
/// path/name/content signals, then applies the configured file/character limits by
/// including whole files (never silently truncating) until a budget would be
/// exceeded. Budgets each file by its <b>effective</b> size — the size the context
/// renderer will actually produce (<see cref="ContextContentPolicy.EffectiveContextCharacters"/>)
/// — so selection accounting and the rendered context always agree. Records
/// selection reasons and exclusion counts. No embeddings.
/// </summary>
public sealed class RuleBasedAnalysisContextSelector : IAnalysisContextSelector
{
    private readonly ContextContentPolicy _policy;

    public RuleBasedAnalysisContextSelector(ContextContentPolicy? policy = null)
        => _policy = policy ?? new ContextContentPolicy();

    public AnalysisContextSelection Select(
        RepositorySnapshot repository, FindingCategory? discipline, CancellationToken cancellationToken = default)
    {
        var total = repository.Files.Count;

        // Rank: repository-wide = source-first by size; discipline = by discipline score.
        IReadOnlyList<ScannedFile> ranked;
        string strategy;
        string signal;

        if (discipline is null)
        {
            strategy = "repository-wide";
            signal = "repository-wide: source files first, largest first";
            ranked = repository.Files
                .OrderByDescending(f => f.Extension == ".cs")
                .ThenByDescending(f => f.LineCount)
                .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            strategy = $"{discipline.Value.ToString().ToLowerInvariant()}-focused-v1";
            signal = $"{discipline.Value}: ranked by discipline path/name/content signals";
            var scored = repository.Files
                .Select(f => (File: f, Score: ScoreFor(discipline.Value, f)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.File.LineCount)
                .ThenBy(x => x.File.RelativePath, StringComparer.Ordinal)
                .Select(x => x.File)
                .ToList();

            // Always give the discipline *some* structural context.
            ranked = scored.Count > 0
                ? scored
                : repository.Files
                    .OrderByDescending(f => f.Extension == ".cs")
                    .ThenByDescending(f => f.LineCount)
                    .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
                    .ToList();
        }

        // Apply limits: include whole files until a budget would be exceeded.
        // Each file counts its EFFECTIVE size (what the renderer will produce), so
        // selection budgeting and the rendered context never disagree.
        var selected = new List<ScannedFile>();
        long chars = 0;
        var excludedByFileLimit = 0;
        var excludedByCharLimit = 0;

        foreach (var file in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = _policy.EffectiveContextCharacters(file);

            if (selected.Count >= _policy.MaxFiles) { excludedByFileLimit++; continue; }
            if (selected.Count > 0 && chars + size > _policy.MaxCharacters) { excludedByCharLimit++; continue; }

            selected.Add(file);
            chars += size;
        }

        var reasons = new List<string> { signal, $"selected {selected.Count} of {ranked.Count} candidate file(s)" };
        if (excludedByFileLimit > 0) reasons.Add($"excluded {excludedByFileLimit} file(s) over the {_policy.MaxFiles}-file limit");
        if (excludedByCharLimit > 0) reasons.Add($"excluded {excludedByCharLimit} file(s) over the {_policy.MaxCharacters}-character limit");

        return new AnalysisContextSelection
        {
            Strategy = strategy,
            Files = selected,
            TotalRepositoryFiles = total,
            SelectedFileCount = selected.Count,
            EstimatedContentSize = chars,
            SelectionReasons = reasons
        };
    }

    private static int ScoreFor(FindingCategory discipline, ScannedFile f)
    {
        var path = f.RelativePath.ToLowerInvariant();
        var name = System.IO.Path.GetFileName(path);
        var content = f.Content ?? string.Empty;

        int InPath(params string[] needles) => needles.Any(path.Contains) ? 3 : 0;
        int InName(params string[] needles) => needles.Any(name.Contains) ? 3 : 0;
        int HasExt(params string[] exts) => exts.Contains(f.Extension) ? 2 : 0;
        int InBody(params string[] needles) => needles.Any(n => content.Contains(n, StringComparison.OrdinalIgnoreCase)) ? 2 : 0;

        return discipline switch
        {
            FindingCategory.Architecture =>
                HasExt(".sln", ".slnx", ".csproj", ".props", ".targets")
                + InName("directory.build.props", "directory.build.targets", "directory.packages.props", "program.cs", "startup.cs", "apphost.cs")
                + InPath("/adr/", "architecture")
                + InBody("namespace ", "ProjectReference", "AddScoped", "AddSingleton", "AddTransient"),
            FindingCategory.CodeQuality =>
                HasExt(".cs") + (f.LineCount > 150 ? 2 : 0) + InName("helper", "util", "utils", "manager")
                + InBody("public class", "private ", "switch "),
            FindingCategory.Reliability =>
                InBody("HttpClient", "Polly", "retry", "timeout", "CancellationToken", "catch", "IHostedService",
                    "BackgroundService", "DbContext", "Queue", "Channel", "Transient"),
            FindingCategory.Security =>
                InName("appsettings.json") + HasExt(".csproj")
                + InPath("controller", "middleware", "auth")
                + InBody("Authentication", "Authorize", "ApiKey", "password", "secret", "Jwt", "Cors",
                    "Cryptography", "HttpsRedirection", "ClientSecret", "PackageReference"),
            FindingCategory.Testing =>
                InPath("test", "tests", "spec", "fixture") + InName("tests.cs") + HasExt(".csproj")
                + InBody("[Fact]", "[Theory]", "Assert.", "xunit", "Moq"),
            FindingCategory.Documentation =>
                HasExt(".md") + InName("readme.md") + InPath("/docs/", "/adr/", "runbook", "deploy")
                + InBody("GenerateDocumentationFile", "<summary>"),
            FindingCategory.Observability =>
                InBody("ILogger", "OpenTelemetry", "Metrics", "ActivitySource", "HealthCheck", "AddHealthChecks",
                    "Aspire", "Serilog", "Tracing", "Dashboard"),
            _ => f.Extension == ".cs" ? 1 : 0
        };
    }
}
