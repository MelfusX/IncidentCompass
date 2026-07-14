using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed class RecurrenceEscalationScheduler(IRecurrenceEscalationReTriageScheduler reTriageScheduler)
{
    public Task ScheduleIfEscalatedAsync(
        RecurrenceAttachmentResult? recurrenceAttachment,
        Fault recurrenceFault,
        CancellationToken cancellationToken)
    {
        if (recurrenceAttachment is null ||
            !recurrenceAttachment.State.EscalationIntentCreatedFor(recurrenceAttachment.Job.Id))
        {
            return Task.CompletedTask;
        }

        return reTriageScheduler.ScheduleAsync(
            recurrenceAttachment.Job,
            recurrenceFault,
            recurrenceAttachment.State,
            cancellationToken);
    }
}