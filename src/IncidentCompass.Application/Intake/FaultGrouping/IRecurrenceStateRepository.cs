namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface IRecurrenceStateRepository
{
    Task<RecurrenceState> RecordAsync(
        RecurrenceOccurrence occurrence,
        CancellationToken cancellationToken);
}