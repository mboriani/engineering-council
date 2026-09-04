using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

var outputsRoot = builder.Configuration["OUTPUTS_ROOT"] ?? "outputs";

// Evidence:Providers may be plain names (["Mock"]) or objects ([{Name,Scope}]).
var (configuredProviders, configuredScopes) = EvidenceConfig.ParseProviders(builder.Configuration);
var (maxFiles, maxChars, maxCharsPerFile) = EvidenceConfig.ParseContextLimits(builder.Configuration);
var configuredDisciplines = EvidenceConfig.ParseDisciplines(builder.Configuration);
var (sarifEnabled, sarifFiles) = EvidenceConfig.ParseSarif(builder.Configuration);

builder.Services.AddEngineeringCouncil(o =>
{
    o.OutputsRoot = outputsRoot;
    if (configuredProviders.Count > 0) o.Providers = configuredProviders;
    o.ProviderScopes = configuredScopes;
    o.Disciplines = configuredDisciplines;
    o.ContextMaxFiles = maxFiles;
    o.ContextMaxCharacters = maxChars;
    o.ContextMaxCharactersPerFile = maxCharsPerFile;
    o.SarifEnabled = sarifEnabled;
    o.SarifFiles = sarifFiles;

    // Real LLM sources (Milestone 011) — disabled by default; keys come from the
    // environment variable named in configuration, never from configuration itself.
    EvidenceConfig.BindLlmProvider(builder.Configuration.GetSection("Evidence:Claude"), o.Claude);
    EvidenceConfig.BindLlmProvider(builder.Configuration.GetSection("Evidence:OpenAI"), o.OpenAi);
    o.ProviderFailureMode = EvidenceConfig.ParseFailureMode(builder.Configuration);
    o.MaxConcurrency = EvidenceConfig.ParseMaxConcurrency(builder.Configuration);
    o.ProviderTimeout = EvidenceConfig.ParseProviderTimeout(builder.Configuration);
    o.MaxAttempts = EvidenceConfig.ParseMaxAttempts(builder.Configuration);

    // Targeted semantic reconciliation (Milestone 014.4): Council:SemanticReconciliation.
    // Disabled by default — offline/default behavior stays identical to M14.3.
    EvidenceConfig.BindSemanticReconciliation(
        builder.Configuration.GetSection(SemanticReconciliationOptions.SectionName), o.SemanticReconciliation);
});

// Swagger / OpenAPI.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new()
{
    Title = "Engineering Council Agent API",
    Version = "v1",
    Description = "Trigger and query engineering review runs."
}));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Root redirects to the Swagger UI so opening the resource from the Aspire dashboard lands somewhere useful.
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", (SelectedProviders providers) => Results.Ok(new
{
    status = "ok",
    providers = providers.Names,
    isMock = providers.IsMock
}));

// Trigger a new analysis run. READ-ONLY over the target repository.
app.MapPost("/runs", async (
    AnalyzeRequestBody body,
    AnalysisPipeline pipeline,
    SelectedProviders providers,
    AnalysisOrchestrator orchestrator,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.TargetPath) || !Directory.Exists(body.TargetPath))
        return Results.BadRequest(new { error = "targetPath does not exist or was not provided." });

    // Validate any requested disciplines BEFORE analysis runs.
    IReadOnlyList<FindingCategory>? disciplines = null;
    if (body.Disciplines is { Count: > 0 })
    {
        var parsed = new List<FindingCategory>();
        var invalid = new List<string>();
        foreach (var token in body.Disciplines)
            if (Enum.TryParse<FindingCategory>(token, ignoreCase: true, out var c)) parsed.Add(c);
            else invalid.Add(token);

        if (invalid.Count > 0)
            return Results.BadRequest(new
            {
                error = $"Unknown discipline(s): {string.Join(", ", invalid)}.",
                valid = Enum.GetNames<FindingCategory>()
            });

        disciplines = parsed.Distinct().ToList();
    }

    // Fail fast (issue A6): a valid-enum discipline with no registered analyzer is
    // rejected with 400, reporting ALL unsupported disciplines, BEFORE analysis runs.
    var unsupported = AnalyzerDisciplineValidator.Unsupported(disciplines, orchestrator.Disciplines);
    if (unsupported.Count > 0)
        return Results.BadRequest(new { error = AnalyzerDisciplineValidator.FormatUnsupported(unsupported) });

    AnalysisResult result;
    try
    {
        result = await pipeline.RunAsync(new AnalysisRequest
        {
            TargetPath = body.TargetPath,
            ProviderName = providers.DisplayName,
            Disciplines = disciplines
        }, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.Json(new { error = "Analysis was cancelled by the client. No review package was produced." },
            statusCode: StatusCodes.Status499ClientClosedRequest);
    }
    catch (UnsupportedDisciplineException ex)
    {
        // Config-driven disciplines (Evidence:Disciplines) are also validated at the
        // pipeline entry; surface them as a client error, never a 500.
        return Results.BadRequest(new { error = ex.Message });
    }

    return Results.Ok(new
    {
        runId = result.Run.RunId,
        status = result.Run.Status.ToString(),
        provider = result.Run.Provider,
        filesScanned = result.Run.FilesScanned,
        findings = result.Run.Findings.Count,
        outputDirectory = result.OutputDirectory
    });
});

app.MapGet("/runs", async (IAnalysisRunRepository repo, CancellationToken ct)
    => Results.Ok(await repo.ListRunIdsAsync(ct)));

app.MapGet("/runs/{runId}", async (string runId, IAnalysisRunRepository repo, CancellationToken ct) =>
{
    if (!AnalysisRunId.IsValid(runId))
        return Results.BadRequest(new { error = "Invalid run id format." });

    var run = await repo.GetAsync(runId, ct);
    return run is null ? Results.NotFound() : Results.Json(run, CouncilJson.Options);
});

app.Run();

internal sealed record AnalyzeRequestBody(string TargetPath, IReadOnlyList<string>? Disciplines = null);

/// <summary>Normalizes the <c>Evidence</c> configuration section (names|objects, context limits, disciplines).</summary>
internal static class EvidenceConfig
{
    public static (IReadOnlyList<string> Names, IReadOnlyDictionary<string, EvidenceAcquisitionScope> Scopes) ParseProviders(
        IConfiguration config)
    {
        var names = new List<string>();
        var scopes = new Dictionary<string, EvidenceAcquisitionScope>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in config.GetSection("Evidence:Providers").GetChildren())
        {
            var name = child["Name"] ?? child.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            names.Add(name);
            if (Enum.TryParse<EvidenceAcquisitionScope>(child["Scope"], ignoreCase: true, out var scope))
                scopes[name] = scope;
        }

        return (names, scopes);
    }

    public static (int MaxFiles, int MaxCharacters, int MaxCharactersPerFile) ParseContextLimits(IConfiguration config)
    {
        var section = config.GetSection("Evidence:Context");
        var maxFiles = int.TryParse(section["MaximumFiles"], out var f) && f > 0 ? f : 200;
        var maxChars = int.TryParse(section["MaximumCharacters"], out var c) && c > 0 ? c : 500_000;
        var maxCharsPerFile = int.TryParse(section["MaximumCharactersPerFile"], out var p) && p > 0 ? p : 8_000;
        return (maxFiles, maxChars, maxCharsPerFile);
    }

    public static IReadOnlyList<FindingCategory> ParseDisciplines(IConfiguration config)
    {
        var result = new List<FindingCategory>();
        foreach (var child in config.GetSection("Evidence:Disciplines").GetChildren())
            if (Enum.TryParse<FindingCategory>(child.Value, ignoreCase: true, out var c))
                result.Add(c);
        return result.Distinct().ToList();
    }

    /// <summary>Binds an <c>Evidence:{Provider}</c> section (never a key value).</summary>
    public static void BindLlmProvider(IConfiguration section, LlmProviderOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["ApiKeyEnvironmentVariable"] is { Length: > 0 } env) options.ApiKeyEnvironmentVariable = env;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (section["Endpoint"] is { Length: > 0 } endpoint) options.Endpoint = endpoint;
        if (int.TryParse(section["MaxOutputTokens"], out var maxTokens) && maxTokens > 0) options.MaxOutputTokens = maxTokens;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
        if (int.TryParse(section["MaxRetries"], out var retries) && retries >= 0) options.MaxRetries = retries;
        if (bool.TryParse(section["EnableStructuredRepair"], out var repair)) options.EnableStructuredRepair = repair;
    }

    public static ProviderFailureMode ParseFailureMode(IConfiguration config)
        => Enum.TryParse<ProviderFailureMode>(config["Evidence:ProviderFailureMode"], ignoreCase: true, out var mode)
            ? mode
            : ProviderFailureMode.Continue;

    /// <summary>Reads Evidence:Execution:MaxConcurrency; defaults to 1 (strictly sequential).</summary>
    public static int ParseMaxConcurrency(IConfiguration config)
        => int.TryParse(config["Evidence:Execution:MaxConcurrency"], out var concurrency) && concurrency >= 1
            ? concurrency
            : 1;

    /// <summary>
    /// Reads Evidence:Execution:ProviderTimeout (seconds); defaults to 120. This is
    /// the FALLBACK step timeout — a provider with its own configured timeout uses
    /// that instead (M15.2B).
    /// </summary>
    public static TimeSpan ParseProviderTimeout(IConfiguration config)
        => TimeSpan.FromSeconds(
            int.TryParse(config["Evidence:Execution:ProviderTimeout"], out var seconds) && seconds > 0
                ? seconds
                : 120);

    /// <summary>
    /// Reads Evidence:Execution:MaxAttempts; defaults to 2 (one bounded retry, M15.3B).
    /// Clamped to [1, EvidenceOptions.MaxAttemptsLimit].
    /// </summary>
    public static int ParseMaxAttempts(IConfiguration config)
        => int.TryParse(config["Evidence:Execution:MaxAttempts"], out var attempts) && attempts >= 1
            ? Math.Min(attempts, EvidenceOptions.MaxAttemptsLimit)
            : EvidenceOptions.DefaultMaxAttempts;

    /// <summary>Binds a <c>Council:SemanticReconciliation</c> section onto the semantic reconciliation options.</summary>
    public static void BindSemanticReconciliation(IConfiguration section, SemanticReconciliationOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (int.TryParse(section["MaxOutputTokens"], out var maxTokens) && maxTokens > 0) options.MaxOutputTokens = maxTokens;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
    }

    public static (bool Enabled, IReadOnlyList<string> Files) ParseSarif(IConfiguration config)
    {
        var section = config.GetSection("Evidence:Sarif");
        var enabled = bool.TryParse(section["Enabled"], out var e) && e;
        var files = section.GetSection("Files").GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        return (enabled, files);
    }
}
