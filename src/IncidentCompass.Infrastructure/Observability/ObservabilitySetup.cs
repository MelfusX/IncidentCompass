using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Infrastructure.Observability;

public static class ObservabilitySetup
{
    public static IServiceCollection AddObservabilityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}
