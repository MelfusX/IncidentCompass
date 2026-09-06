using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Tenancy;
using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Application.Investigation.Reports.GetLatest;

public sealed class GetLatestTriageReportQueryHandler(ITriageReportReadRepository repository, IIncidentTenantContext incidentTenantContext)
    : IRequestHandler<GetLatestTriageReportQuery, TriageReportDetailsResponse>
{
    public async Task<TriageReportDetailsResponse> HandleAsync(
        GetLatestTriageReportQuery request,
        CancellationToken cancellationToken)
    {
        return await repository.FindLatestByFaultIdAsync(request.FaultId, await incidentTenantContext.GetTenantIdAsync(cancellationToken), cancellationToken)
            ?? throw new NotFoundException(
                $"Triage report for fault '{request.FaultId}' was not found.",
                ApplicationErrorCodes.TriageReportNotFound,
                "The requested triage report does not exist.");
    }
}
