namespace IncidentCompass.Application.Intake.GetFault;

public sealed record FaultDetailsResponse(
    Guid Id,
    string Status,
    string Fingerprint,
    int FingerprintVersion,
    string FingerprintStrength,
    bool CanGroup,
    string ServiceName,
    string Environment,
    string? Severity,
    string? CorrelationId,
    Guid TriggerSignalId,
    Guid? RecurrenceOf,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TriageJobSummary? Job);
