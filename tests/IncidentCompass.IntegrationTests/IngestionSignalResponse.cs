namespace IncidentCompass.IntegrationTests;

internal sealed record IngestionSignalResponse(
    Guid SignalId,
    Guid FaultId,
    bool IsNewFault,
    bool IsNewJob,
    bool IsSuppressed,
    Guid? JobId,
    string? ConfigHash);
