namespace IncidentCompass.IntegrationTests;

internal sealed record TriageReportIngestResponse(
    Guid SignalId,
    Guid FaultId,
    bool IsNewFault,
    bool IsNewJob,
    bool IsSuppressed,
    Guid? JobId,
    string? ConfigHash);
