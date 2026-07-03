using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;

namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed class GetTriageReportQueryHandler(ITriageReportReadRepository repository)
    : IRequestHandler<GetTriageReportQuery, TriageReportDetailsResponse>
{
    public async Task<TriageReportDetailsResponse> HandleAsync(
        GetTriageReportQuery request,
        CancellationToken cancellationToken)
    {
        return await repository.FindByIdAsync(request.ReportId, cancellationToken)
            ?? throw new NotFoundException($"Triage report '{request.ReportId}' was not found.");
    }
}
