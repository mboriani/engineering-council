namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Resolves an <see cref="IEvidenceProvider"/> by name. Analyzers request a
/// provider through the factory (by configuration) rather than depending on any
/// concrete provider, so new providers can be added without touching analyzers.
/// </summary>
public interface IEvidenceProviderFactory
{
    /// <summary>All registered provider names.</summary>
    IReadOnlyCollection<string> ProviderNames { get; }

    /// <summary>
    /// Resolves the provider with the given name (case-insensitive).
    /// Throws <see cref="InvalidOperationException"/> if no such provider is registered.
    /// </summary>
    IEvidenceProvider GetProvider(string name);

    bool TryGetProvider(string name, out IEvidenceProvider provider);
}
