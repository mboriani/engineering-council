using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Evidence;

// The following providers exist so the shape (naming, type, metadata,
// registration) is in place; they are NOT implemented yet. Each reports
// IsAvailable = false, so the acquisition planner never plans them and their
// CollectAsync is never called during normal discovery. Static sources declare
// Repository scope (they analyze the whole tree once); LLM sources declare
// Discipline scope. New providers require no change to analyzers or the planner.

/// <summary>Base for not-yet-implemented providers. Identity lives on <see cref="Metadata"/>.</summary>
public abstract class NotImplementedEvidenceProvider : IEvidenceProvider
{
    public abstract EvidenceProviderMetadata Metadata { get; }

    /// <summary>Not available until implemented — so it is never planned or called.</summary>
    public bool IsAvailable => false;

    public Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(
            $"The '{Metadata.Name}' evidence provider is not implemented yet (planned for a future milestone).");
}

/// <summary>Metadata factory for the two planned source shapes.</summary>
file static class PlannedSource
{
    public static EvidenceProviderMetadata Repository(string name, EvidenceProviderType type) => new()
    {
        Name = name,
        ProviderType = type,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
        RequiresAnalyzerInstructions = false,
        SupportsRepositoryWideAnalysis = true
    };

    public static EvidenceProviderMetadata Discipline(string name, EvidenceProviderType type) => new()
    {
        Name = name,
        ProviderType = type,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
        RequiresAnalyzerInstructions = true,
        SupportsRepositoryWideAnalysis = false
    };
}

// NOTE: the former "Codex" stub was retired in Milestone 011. A code-focused OpenAI
// model is simply the configured model of the real OpenAiEvidenceProvider, selected as
// "OpenAI". "Codex" is NOT an alias for OpenAI (Milestone 013.4): it now selects the
// real agentic Codex CLI provider.

/// <summary>Ollama local models (LLM) — planned. Discipline-scoped.</summary>
public sealed class OllamaEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Discipline("Ollama", EvidenceProviderType.LLM);
}

/// <summary>Roslyn analyzers (static analysis) — planned. Repository-scoped.</summary>
public sealed class RoslynEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("Roslyn", EvidenceProviderType.StaticAnalyzer);
}

/// <summary>SonarQube (static analysis) — planned. Repository-scoped.</summary>
public sealed class SonarEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("Sonar", EvidenceProviderType.StaticAnalyzer);
}

/// <summary>Semgrep (static analysis) — planned. Repository-scoped.</summary>
public sealed class SemgrepEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("Semgrep", EvidenceProviderType.StaticAnalyzer);
}

/// <summary>NDepend (static analysis) — planned. Repository-scoped.</summary>
public sealed class NDependEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("NDepend", EvidenceProviderType.StaticAnalyzer);
}

/// <summary>Git history / churn signals (repository metadata) — planned. Repository-scoped.</summary>
public sealed class GitEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("Git", EvidenceProviderType.Custom);
}

/// <summary>Test coverage report ingestion — planned. Repository-scoped.</summary>
public sealed class CoverageEvidenceProvider : NotImplementedEvidenceProvider
{
    public override EvidenceProviderMetadata Metadata => PlannedSource.Repository("Coverage", EvidenceProviderType.Custom);
}
