namespace EngineeringCouncil.Core.Domain;

public enum AnalysisRunStatus
{
    Pending = 0,
    Scanning = 1,
    Analyzing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
