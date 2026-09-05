namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed record ExistingSignalDelivery(
    Guid SignalId,
    Guid FaultId,
    bool IsSuppressed,
    Guid? JobId,
    string? ConfigHash);
