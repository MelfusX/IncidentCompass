using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Reports;

public interface IMinimalTriageReportRepository
{
    Task<Guid> PublishAsync(
        TriageJob job,
        string workerId,
        MinimalTriageReport report,
        CancellationToken cancellationToken);
}
