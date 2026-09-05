using IncidentCompass.Application.Governance.PostReportActions;

namespace IncidentCompass.IntegrationTests;

internal sealed class ThrowingReportPublicationIntentFaultInjector(
    ReportPublicationIntentFaultPoint faultPoint) : ITriageReportPublicationIntentFaultInjector
{
    public Task AfterIntentInsertedAsync(CancellationToken cancellationToken)
    {
        if (faultPoint == ReportPublicationIntentFaultPoint.AfterIntentInserted)
        {
            throw new InvalidOperationException("Injected failure after post-report action intent insert.");
        }

        return Task.CompletedTask;
    }
}
