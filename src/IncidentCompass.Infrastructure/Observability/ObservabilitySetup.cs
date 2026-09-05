using IncidentCompass.Application.Observability.CostRollup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Infrastructure.Observability;

public static class ObservabilitySetup
{
    public static IServiceCollection AddObservabilityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.TryAddScoped<IModelCostRollupRepository, PostgresModelCostRollupRepository>();
        return services;
    }
}
