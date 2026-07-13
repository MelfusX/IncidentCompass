using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Application.Investigation.Reports;

public interface ITriageReportReadRepository
{
    Task<TriageReportDetailsResponse?> FindByIdAsync(Guid reportId, CancellationToken cancellationToken);

    Task<TriageReportDetailsResponse?> FindLatestByFaultIdAsync(Guid faultId, CancellationToken cancellationToken);
}
