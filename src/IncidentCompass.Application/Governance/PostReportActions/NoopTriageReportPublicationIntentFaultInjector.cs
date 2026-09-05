namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed class NoopTriageReportPublicationIntentFaultInjector
    : ITriageReportPublicationIntentFaultInjector
{
    public Task AfterIntentInsertedAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
