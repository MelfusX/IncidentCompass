using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Tenancy;

namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed class GetTriageReportQueryHandler(ITriageReportReadRepository repository, IIncidentTenantContext incidentTenantContext)
    : IRequestHandler<GetTriageReportQuery, TriageReportDetailsResponse>
{
    public async Task<TriageReportDetailsResponse> HandleAsync(
        GetTriageReportQuery request,
        CancellationToken cancellationToken)
    {
        return await repository.FindByIdAsync(request.ReportId, await incidentTenantContext.GetTenantIdAsync(cancellationToken), cancellationToken)
            ?? throw new NotFoundException(
                $"Triage report '{request.ReportId}' was not found.",
                ApplicationErrorCodes.TriageReportNotFound,
                "The requested triage report does not exist.");
    }
}
