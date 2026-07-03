using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Reports;

public interface ITriageReportRepository
{
    Task<Guid> PublishAsync(
        TriageJob job,
        string workerId,
        TriageReport report,
        CancellationToken cancellationToken);
}
