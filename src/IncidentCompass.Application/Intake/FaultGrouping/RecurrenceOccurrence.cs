namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed record RecurrenceOccurrence(
    Guid JobId,
    Guid FaultId,
    string TenantId,
    string ServiceName,
    string Environment,
    string Fingerprint,
    int FingerprintVersion,
    string GroupingRuleId,
    int GroupingRuleVersion,
    DateTimeOffset OccurredAtUtc,
    int EscalateAfterCount);