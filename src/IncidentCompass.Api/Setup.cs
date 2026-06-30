using IncidentCompass.Api.Security;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Api;

public static class Setup
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddApiUserContext(configuration, environment);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddHealthChecks();
        services.AddOpenApi();

        return services;
    }
}
