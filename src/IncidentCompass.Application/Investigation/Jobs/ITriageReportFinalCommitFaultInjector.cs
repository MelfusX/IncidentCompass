namespace IncidentCompass.Application.Investigation.Jobs;

public interface ITriageReportFinalCommitFaultInjector
{
    Task BeforeReportPublishedLedgerEventAsync(CancellationToken cancellationToken);
}
