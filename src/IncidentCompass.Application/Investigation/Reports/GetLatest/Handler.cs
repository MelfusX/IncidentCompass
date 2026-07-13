using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Application.Investigation.Reports.GetLatest;

public sealed class GetLatestTriageReportQueryHandler(ITriageReportReadRepository repository)
    : IRequestHandler<GetLatestTriageReportQuery, TriageReportDetailsResponse>
{
    public async Task<TriageReportDetailsResponse> HandleAsync(
        GetLatestTriageReportQuery request,
        CancellationToken cancellationToken)
    {
        return await repository.FindLatestByFaultIdAsync(request.FaultId, cancellationToken)
            ?? throw new NotFoundException($"Triage report for fault '{request.FaultId}' was not found.");
    }
}
