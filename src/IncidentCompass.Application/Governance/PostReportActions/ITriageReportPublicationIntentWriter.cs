using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Governance.PostReportActions;

public interface ITriageReportPublicationIntentWriter
{
    Task WriteAsync(
        TriageJob job,
        Guid reportId,
        string tenantId,
        string serviceName,
        string environment,
        string? severity,
        DateTimeOffset createdAtUtc,
        Func<PostReportActionIntent, CancellationToken, Task> persistAsync,
        CancellationToken cancellationToken);
}
