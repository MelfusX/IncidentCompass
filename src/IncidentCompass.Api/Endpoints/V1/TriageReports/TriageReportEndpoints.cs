using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Investigation.Reports.Get;
using IncidentCompass.Application.Investigation.Reports.List;
using IncidentCompass.Application.Investigation.Reports.GetLatest;

namespace IncidentCompass.Api;

internal static class TriageReportEndpoints
{
    public static RouteGroupBuilder MapTriageReportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/triage-reports", async (
                Guid? faultId,
                string? serviceName,
                string? environment,
                string? status,
                string? classification,
                int? limit,
                string? cursor,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<ListTriageReportsQuery, TriageReportListResponse>(
                    new ListTriageReportsQuery(
                        faultId, serviceName, environment, status, classification, limit, cursor),
                    cancellationToken);
                return Results.Ok(result);
            })
            .WithName("ListTriageReports")
            .WithSummary("List compact tenant-scoped triage report summaries with stable keyset pagination.")
            .Produces<TriageReportListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        api.MapGet("/triage-reports/{id:guid}", async (
                Guid id,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<GetTriageReportQuery, TriageReportDetailsResponse>(
                    new GetTriageReportQuery(id),
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("GetTriageReportById")
            .WithSummary("Return a triage report with backend-grounded evidence.")
            .Produces<TriageReportDetailsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/faults/{faultId:guid}/triage-report", async (
                Guid faultId,
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<GetLatestTriageReportQuery, TriageReportDetailsResponse>(
                    new GetLatestTriageReportQuery(faultId),
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("GetLatestTriageReportForFault")
            .WithSummary("Return the latest triage report for a fault.")
            .Produces<TriageReportDetailsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
