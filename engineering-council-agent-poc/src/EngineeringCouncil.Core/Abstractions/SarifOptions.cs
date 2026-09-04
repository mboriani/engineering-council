namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for the native SARIF evidence source. Bound from the
/// <c>Evidence:Sarif</c> configuration section and/or the CLI (<c>--sarif</c>).
/// The provider does not run scanners; it imports existing SARIF result files.
/// </summary>
public sealed class SarifOptions
{
    public const string SectionName = "Evidence:Sarif";

    /// <summary>When false, the SARIF source is inert even if files are listed.</summary>
    public bool Enabled { get; set; }

    /// <summary>Paths to SARIF 2.1.0 result files to import (one or many).</summary>
    public IReadOnlyList<string> Files { get; set; } = [];

    /// <summary>The source is usable when enabled and at least one file is configured.</summary>
    public bool IsActive => Enabled && Files.Count > 0;
}
