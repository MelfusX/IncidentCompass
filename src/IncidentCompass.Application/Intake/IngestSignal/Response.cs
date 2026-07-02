namespace IncidentCompass.Application.Intake.IngestSignal;

public sealed record IngestSignalResponse(
    Guid SignalId,
    Guid FaultId,
    bool IsNewFault,
    bool IsNewJob,
    bool IsSuppressed,
    Guid? JobId,
    string? ConfigHash);
