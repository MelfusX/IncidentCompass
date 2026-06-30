using IncidentCompass.Application.Core.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Core.Health;

internal static class HealthSetup
{
    public static IServiceCollection AddHealthCore(this IServiceCollection services)
    {
        services.TryAddScoped<IRequestHandler<GetHealthStatusQuery, HealthStatus>, GetHealthStatusHandler>();

        return services;
    }
}
