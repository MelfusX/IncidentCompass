namespace IncidentCompass.Domain.Incidents;

public sealed record TriageJob(
    Guid Id,
    Guid FaultId,
    TriageJobStatus Status,
    int Attempt,
    string? LockedBy,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastErrorCode,
    string? LastErrorMessage,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
