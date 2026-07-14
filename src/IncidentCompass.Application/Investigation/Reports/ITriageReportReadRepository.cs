using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Application.Investigation.Reports;

public interface ITriageReportReadRepository
{
    Task<TriageReportDetailsResponse?> FindByIdAsync(Guid reportId, string tenantId, CancellationToken cancellationToken);

    Task<TriageReportDetailsResponse?> FindLatestByFaultIdAsync(Guid faultId, string tenantId, CancellationToken cancellationToken);
}
