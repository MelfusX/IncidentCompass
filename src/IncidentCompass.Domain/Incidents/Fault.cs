namespace IncidentCompass.Domain.Incidents;

public sealed record Fault(
    Guid Id,
    Guid TriggerSignalId,
    string TenantId,
    FaultStatus Status,
    string Fingerprint,
    int FingerprintVersion,
    FingerprintStrength FingerprintStrength,
    bool CanGroup,
    string ServiceName,
    string Environment,
    string? Severity,
    string? CorrelationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? RecurrenceOf)
{
    public string GroupingRuleId { get; init; } = "default";
    public int GroupingRuleVersion { get; init; } = 1;
}
