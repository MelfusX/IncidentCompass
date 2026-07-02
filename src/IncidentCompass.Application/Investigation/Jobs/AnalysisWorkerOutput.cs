namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record AnalysisWorkerOutput(
    IReadOnlyCollection<string> KeyFacts,
    string CandidateClassification,
    bool NeedsDeeperContext,
    string? Rationale);
