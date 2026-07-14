using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface IRecurrenceEscalationReTriageScheduler
{
    Task ScheduleAsync(
        TriageJob recurrenceJob,
        Fault recurrenceFault,
        RecurrenceState recurrenceState,
        CancellationToken cancellationToken);
}