using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Api;

internal static class TriageReportEndpoints
{
    public static RouteGroupBuilder MapTriageReportEndpoints(this RouteGroupBuilder api)
    {
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

        return api;
    }
}
