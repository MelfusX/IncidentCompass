using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Health;

namespace IncidentCompass.Api;

internal static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/health", async (
                IApplicationDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.DispatchAsync<GetHealthStatusQuery, HealthStatus>(
                    new GetHealthStatusQuery("api"),
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("GetApiV1Health")
            .WithSummary("Liveness probe for the API host.")
            .Produces<HealthStatus>(StatusCodes.Status200OK);

        return api;
    }
}
