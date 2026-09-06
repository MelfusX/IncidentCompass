namespace IncidentCompass.Application.Investigation.Jobs.Testing;

/// <summary>
/// Production binding for <see cref="ITriageReportFinalCommitFaultInjector"/>. It never faults; the
/// consuming repository needs something to call at the seam.
/// </summary>
internal sealed class NoopTriageReportFinalCommitFaultInjector : ITriageReportFinalCommitFaultInjector
{
    public Task BeforeReportPublishedLedgerEventAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
