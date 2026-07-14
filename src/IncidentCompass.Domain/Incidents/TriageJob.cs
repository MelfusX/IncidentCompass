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
    DateTimeOffset UpdatedAtUtc)
{
    public Guid? ReTriageTriggerJobId { get; init; }

    public Guid? SupersedesReportId { get; init; }

    public bool IsReTriage => ReTriageTriggerJobId is not null;
}
