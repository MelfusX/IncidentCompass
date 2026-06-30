using IncidentCompass.Application;
using IncidentCompass.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.UnitTests;

internal static class TestApplicationComposition
{
    public static IServiceCollection AddTestApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApplication(configuration);
        services.AddObservabilityInfrastructure(configuration);

        return services;
    }
}
