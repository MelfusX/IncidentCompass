using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed class RecurrenceTracker(IRecurrenceStateRepository recurrenceStateRepository)
{
    public Task<RecurrenceState?> TrackAsync(
        Fault fault,
        Signal triggerSignal,
        TriageJob job,
        FaultGroupingSettings settings,
        CancellationToken cancellationToken)
    {
        if (fault.RecurrenceOf is null || !triggerSignal.CanGroup)
        {
            return Task.FromResult<RecurrenceState?>(null);
        }

        var recurrence = new RecurrenceOccurrence(
            job.Id,
            fault.Id,
            triggerSignal.TenantId,
            triggerSignal.ServiceName,
            triggerSignal.Environment,
            triggerSignal.Fingerprint!,
            triggerSignal.FingerprintVersion!.Value,
            triggerSignal.GroupingRuleId,
            triggerSignal.GroupingRuleVersion,
            fault.CreatedAtUtc,
            settings.Recurrence?.EscalateAfterCount ?? 0);
        return RecordAsync(recurrence, cancellationToken);
    }

    private async Task<RecurrenceState?> RecordAsync(
        RecurrenceOccurrence recurrence,
        CancellationToken cancellationToken) =>
        await recurrenceStateRepository.RecordAsync(recurrence, cancellationToken);
}