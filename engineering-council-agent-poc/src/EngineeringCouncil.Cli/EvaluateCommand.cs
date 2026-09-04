using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EngineeringCouncil.Cli;

/// <summary>
/// The <c>evaluate</c> verb (Milestone 012): runs the EXISTING pipeline over the
/// evaluation dataset and writes an internal <c>evaluation-report.md</c>. It measures
/// the platform — it adds no analysis capability and never changes what
/// <c>engineering-review-package.json</c> contains.
/// </summary>
internal static class EvaluateCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = EvaluateArgs.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        if (options.InvalidDisciplines.Count > 0)
        {
            Console.Error.WriteLine(
                $"Unknown discipline(s): {string.Join(", ", options.InvalidDisciplines)}. "
                + $"Valid disciplines: {string.Join(", ", Enum.GetNames<FindingCategory>())}.");
            return 1;
        }

        var repositories = EvaluationRunner.LoadDataset(options.DatasetRoot);
        if (repositories.Count == 0)
        {
            Console.Error.WriteLine($"No evaluation repositories found under '{options.DatasetRoot}'.");
            return 1;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            b.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("evaluate");

        // --compare X --compare Y → each provider runs SEPARATELY over the same
        // repositories (identical inputs), which is what makes comparison meaningful.
        IReadOnlyList<IReadOnlyList<string>> selections = options.Compare.Count > 0
            ? options.Compare.Select(p => (IReadOnlyList<string>)[p]).ToList()
            : [options.Providers];

        var providers = new List<ServiceProvider>();
        try
        {
            var runner = new EvaluationRunner(
                evaluationCase =>
                {
                    var sp = BuildServices(evaluationCase, configuration, options);
                    providers.Add(sp);
                    return sp.GetRequiredService<AnalysisPipeline>();
                },
                ParsePricing(configuration),
                logger);

            var report = await runner.RunAsync(repositories, selections);

            Directory.CreateDirectory(options.OutputsRoot);
            var reportPath = Path.Combine(Path.GetFullPath(options.OutputsRoot), "evaluation-report.md");
            await File.WriteAllTextAsync(reportPath, new EvaluationReportExporter().Export(report));

            Console.WriteLine();
            Console.WriteLine("Evaluation complete");
            Console.WriteLine($"  Repositories: {report.RepositoriesEvaluated.Count}");
            Console.WriteLine($"  Runs:         {report.Runs.Count}");
            Console.WriteLine($"  Providers:    {string.Join(", ", report.ProvidersEvaluated)}");
            Console.WriteLine($"  Completed:    {report.Runs.Count(r => r.Status == "Completed")}/{report.Runs.Count}");
            Console.WriteLine($"  Report:       {reportPath}");

            if (report.ProvidersUnavailable.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Provider executions that could not run:");
                foreach (var reason in report.ProvidersUnavailable.Take(6))
                    Console.WriteLine($"    - {reason}");
            }

            return 0;
        }
        finally
        {
            foreach (var sp in providers) sp.Dispose();
        }
    }

    /// <summary>Composes the normal DI graph for one evaluation case.</summary>
    private static ServiceProvider BuildServices(
        EvaluationCase evaluationCase, IConfiguration configuration, EvaluateArgs options)
    {
        // SARIF is only activated when the selection asks for it AND the repository ships
        // a SARIF file. Where there is no file, SARIF is dropped from the selection so a
        // repository without one is not counted as a provider failure.
        var wantsSarif = evaluationCase.Providers.Any(p => string.Equals(p, "Sarif", StringComparison.OrdinalIgnoreCase));
        var sarifFile = wantsSarif && evaluationCase.Repository.Sarif is { } path && File.Exists(path) ? path : null;
        var selection = sarifFile is null
            ? evaluationCase.Providers.Where(p => !string.Equals(p, "Sarif", StringComparison.OrdinalIgnoreCase)).ToList()
            : evaluationCase.Providers.ToList();
        if (selection.Count == 0) selection.Add("Mock");

        return new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddEngineeringCouncil(o =>
            {
                o.OutputsRoot = Path.Combine(options.OutputsRoot, "runs");
                o.Providers = selection;
                o.Disciplines = options.Disciplines;
                o.SarifEnabled = sarifFile is not null;
                o.SarifFiles = sarifFile is null ? [] : [sarifFile];
                CliArgs.BindLlmProvider(configuration.GetSection("Evidence:Claude"), o.Claude);
                CliArgs.BindLlmProvider(configuration.GetSection("Evidence:OpenAI"), o.OpenAi);
            })
            .BuildServiceProvider();
    }

    /// <summary>
    /// Optional cost pricing from <c>Evaluation:Pricing:{provider/model}</c>. Pricing is
    /// configuration only — absent pricing means cost is reported as unavailable, never guessed.
    /// </summary>
    private static IReadOnlyDictionary<string, ModelPricing>? ParsePricing(IConfiguration configuration)
    {
        var section = configuration.GetSection("Evaluation:Pricing");
        var pricing = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in section.GetChildren())
        {
            if (decimal.TryParse(entry["InputPerMillion"], out var input)
                && decimal.TryParse(entry["OutputPerMillion"], out var output))
                pricing[entry.Key] = new ModelPricing(input, output, entry["Currency"] ?? "USD");
        }

        return pricing.Count == 0 ? null : pricing;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Engineering Council Agent — evaluation (internal calibration)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  evaluate --dataset <dir> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --dataset      Dataset root (default: ./evaluation/dataset).");
        Console.WriteLine("  --outputs      Where to write evaluation-report.md (default: outputs/evaluations).");
        Console.WriteLine("  --providers    Comma-separated providers for ONE combined selection (e.g. Mock,Sarif).");
        Console.WriteLine("  --compare      A provider to evaluate on its own (repeatable) — identical inputs per repository.");
        Console.WriteLine("  --disciplines  Comma-separated disciplines (default: all).");
        Console.WriteLine("  --discipline   A single discipline (repeatable).");
    }

    private sealed record EvaluateArgs(
        string DatasetRoot, string OutputsRoot, IReadOnlyList<string> Providers, IReadOnlyList<string> Compare,
        IReadOnlyList<FindingCategory> Disciplines, IReadOnlyList<string> InvalidDisciplines)
    {
        public static EvaluateArgs? Parse(string[] args)
        {
            var dataset = Path.Combine("evaluation", "dataset");
            var outputs = Path.Combine("outputs", "evaluations");
            List<string> providers = [];
            List<string> compare = [];
            List<FindingCategory> disciplines = [];
            List<string> invalid = [];

            for (var i = 1; i < args.Length; i++)   // args[0] is "evaluate"
            {
                switch (args[i])
                {
                    case "--dataset" when i + 1 < args.Length: dataset = args[++i]; break;
                    case "--outputs" or "-o" when i + 1 < args.Length: outputs = args[++i]; break;
                    case "--providers" when i + 1 < args.Length:
                        providers.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case "--provider" when i + 1 < args.Length: providers.Add(args[++i]); break;
                    case "--compare" when i + 1 < args.Length: compare.Add(args[++i]); break;
                    case "--disciplines" when i + 1 < args.Length:
                        foreach (var token in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            Add(token, disciplines, invalid);
                        break;
                    case "--discipline" when i + 1 < args.Length:
                        Add(args[++i], disciplines, invalid);
                        break;
                    case "-h" or "--help": return null;
                }
            }

            if (providers.Count == 0 && compare.Count == 0) providers.Add("Mock");

            return new EvaluateArgs(dataset, outputs,
                providers.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                compare.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                disciplines.Distinct().ToList(), invalid);
        }

        private static void Add(string token, List<FindingCategory> disciplines, List<string> invalid)
        {
            if (Enum.TryParse<FindingCategory>(token, ignoreCase: true, out var parsed)) disciplines.Add(parsed);
            else invalid.Add(token);
        }
    }
}
