using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

internal sealed class DeferredRecurrenceEscalationReTriageScheduler : IRecurrenceEscalationReTriageScheduler
{
    public Task ScheduleAsync(
        TriageJob recurrenceJob,
        Fault recurrenceFault,
        RecurrenceState recurrenceState,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A recurrence escalation re-triage scheduler has not been configured.");
}
