namespace IncidentCompass.Application.Governance.PostReportActions;

public interface ITriageReportPublicationIntentFaultInjector
{
    Task AfterIntentInsertedAsync(CancellationToken cancellationToken);
}
