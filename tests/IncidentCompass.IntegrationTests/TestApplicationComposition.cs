using IncidentCompass.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

internal static class TestApplicationComposition
{
    public static IServiceCollection AddTestApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApplication(configuration);

        return services;
    }
}
