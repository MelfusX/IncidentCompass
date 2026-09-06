namespace IncidentCompass.Application.Governance.PostReportActions.Testing;

/// <summary>
/// Production binding for <see cref="ITriageReportPublicationIntentFaultInjector"/>. It never faults;
/// the consuming report repository needs something to call at the seam.
/// </summary>
internal sealed class NoopTriageReportPublicationIntentFaultInjector
    : ITriageReportPublicationIntentFaultInjector
{
    public Task AfterIntentInsertedAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
