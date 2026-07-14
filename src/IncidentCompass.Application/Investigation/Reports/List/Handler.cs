using FluentValidation.Results;
using System.Buffers.Binary;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Tenancy;

namespace IncidentCompass.Application.Investigation.Reports.List;

public sealed class ListTriageReportsQueryHandler(
    ITriageReportListRepository repository,
    IIncidentTenantContext incidentTenantContext)
    : IRequestHandler<ListTriageReportsQuery, TriageReportListResponse>
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 100;

    public async Task<TriageReportListResponse> HandleAsync(
        ListTriageReportsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaximumLimit)
        {
            throw new RequestValidationException([new ValidationFailure("limit", $"must be between 1 and {MaximumLimit}.")]);
        }

        var cursor = TriageReportListCursor.Decode(request.Cursor);
        var reports = await repository.ListAsync(
            new TriageReportListFilter(
                request.FaultId,
                request.ServiceName,
                request.Environment,
                request.Status,
                request.Classification,
                cursor?.CreatedAtUtc,
                cursor?.ReportId,
                limit + 1),
            await incidentTenantContext.GetTenantIdAsync(cancellationToken),
            cancellationToken);
        var page = reports.Take(limit).ToArray();
        var nextCursor = reports.Count > limit
            ? TriageReportListCursor.Encode(page[^1].CreatedAtUtc, page[^1].Id)
            : null;
        return new TriageReportListResponse(page, nextCursor);
    }
}