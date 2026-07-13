using IncidentCompass.Api.Health;
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
        services.AddScoped<OtlpPayloadReader>();
        services.AddApiUserContext(configuration, environment);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<MemorySeedSyncHealthCheck>("memory_seed_sync", tags: ["ready"]);
        services.AddOpenApi();

        return services;
    }
}
