namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed record RecurrenceState(
    int Count,
    DateTimeOffset FirstOccurredAtUtc,
    DateTimeOffset LastOccurredAtUtc,
    Guid? EscalationIntentJobId,
    Guid? EscalationIntentFaultId)
{
    public bool EscalationIntentCreatedFor(Guid jobId) => EscalationIntentJobId == jobId;
}