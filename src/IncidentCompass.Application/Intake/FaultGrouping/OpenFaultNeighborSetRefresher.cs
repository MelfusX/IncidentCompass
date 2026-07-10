using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed class OpenFaultNeighborSetRefresher(
    ISignalRepository signalRepository,
    ITriageJobRepository triageJobRepository,
    GroundedFactsAssembler groundedFactsAssembler)
{
    public async Task RefreshAsync(
        Fault openFault,
        Signal signal,
        TriageConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var job = await triageJobRepository.FindByFaultIdAsync(openFault.Id, cancellationToken);
        if (job is null ||
            job.Status is TriageJobStatus.Succeeded or TriageJobStatus.Failed or TriageJobStatus.DeadLettered)
        {
            return;
        }

        var neighborCount = signal.CanGroup
            ? await FaultGroupingMetrics.CountNeighborsAsync(
                signalRepository,
                signal,
                configuration.FaultGrouping,
                cancellationToken)
            : 0;
        var isMassIssue = FaultGroupingMetrics.DetermineIsMassIssue(
            signal,
            neighborCount,
            configuration.FaultGrouping);
        await groundedFactsAssembler.ReplaceNeighborSetAsync(
            job, openFault, signal, neighborCount, isMassIssue, configuration.FaultGrouping, cancellationToken);
    }
}
