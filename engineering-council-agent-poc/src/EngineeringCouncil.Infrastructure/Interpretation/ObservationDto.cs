namespace EngineeringCouncil.Infrastructure.Interpretation;

/// <summary>Wire shape produced by LLM-style providers; mapped to observations.</summary>
internal sealed class ObservationsEnvelope
{
    public List<ObservationDto> Observations { get; set; } = [];
}

internal sealed class ObservationDto
{
    public string? Type { get; set; }
    public string? Discipline { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Severity { get; set; }
    public string? Confidence { get; set; }
    public string? RuleId { get; set; }
    public List<FileRefDto>? FileReferences { get; set; }
    public List<string>? SymbolReferences { get; set; }
    public List<int>? LineReferences { get; set; }
    public string? EvidenceExcerpt { get; set; }
    public string? RecommendationHint { get; set; }
    public List<string>? Tags { get; set; }
}

internal sealed class FileRefDto
{
    public string? Path { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
}
