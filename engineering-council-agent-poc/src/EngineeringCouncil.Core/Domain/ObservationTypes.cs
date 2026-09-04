namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Well-known observation type names. Deliberately a set of string constants
/// (not a closed enum) so heterogeneous sources — SonarQube, Roslyn, SARIF,
/// Semgrep, NDepend, Git, coverage, LLMs — can introduce new types without a
/// breaking domain change. Interpreters may emit any string; these are the
/// common ones the platform understands today.
/// </summary>
public static class ObservationTypes
{
    public const string CircularDependency = "CircularDependency";
    public const string LayerViolation = "LayerViolation";
    public const string HighComplexity = "HighComplexity";
    public const string MissingTimeout = "MissingTimeout";
    public const string MissingRetryPolicy = "MissingRetryPolicy";
    public const string BroadExceptionHandling = "BroadExceptionHandling";
    public const string HardcodedSecret = "HardcodedSecret";
    public const string MissingAuthorization = "MissingAuthorization";
    public const string MissingTests = "MissingTests";
    public const string MissingHealthCheck = "MissingHealthCheck";
    public const string MissingDocumentation = "MissingDocumentation";
    public const string CodeHotspot = "CodeHotspot";

    /// <summary>Used when an interpreter cannot map to a more specific type.</summary>
    public const string GeneralObservation = "GeneralObservation";

    /// <summary>
    /// A deterministic source (e.g. a SARIF rule) whose meaning is not recognized by
    /// any mapping. The type is left Unknown rather than invented.
    /// </summary>
    public const string Unknown = "Unknown";
}
