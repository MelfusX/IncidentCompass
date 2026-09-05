using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Observability.CostRollup;

namespace IncidentCompass.Api;

internal static class ObservabilityEndpoints
{
    public static RouteGroupBuilder MapObservabilityEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/observability/cost-rollups", GetCostRollupsAsync)
            .WithName("GetModelCostRollups")
            .WithTags("observability")
            .WithSummary("Return tenant-scoped UTC hourly model-call usage and priced spend totals.")
            .Produces<CostRollupResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        return api;
    }

    private static async Task<IResult> GetCostRollupsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IApplicationDispatcher dispatcher,
        CancellationToken cancellationToken) =>
        Results.Ok(await dispatcher.DispatchAsync<CostRollupQuery, CostRollupResponse>(
            new CostRollupQuery(fromUtc, toUtc),
            cancellationToken));
}
