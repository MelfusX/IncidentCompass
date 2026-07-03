namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class NoopTriageReportFinalCommitFaultInjector : ITriageReportFinalCommitFaultInjector
{
    public Task BeforeReportPublishedLedgerEventAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
