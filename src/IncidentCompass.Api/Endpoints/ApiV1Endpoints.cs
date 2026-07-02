namespace IncidentCompass.Api;

internal static class ApiV1Endpoints
{
    public static IEndpointRouteBuilder MapApiV1(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("v1");

        api.MapHealthEndpoints();
        api.MapUserEndpoints();
        api.MapIncidentEndpoints();

        return endpoints;
    }
}
