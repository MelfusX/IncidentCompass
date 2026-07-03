using IncidentCompass.Application.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Infrastructure.Memory;

internal static class MemoryInfrastructureSetup
{
    public static IServiceCollection AddMemoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MemorySeedOptions>(configuration.GetSection(MemorySeedOptions.SectionName));
        services.TryAddScoped<IMemoryRepository, PostgresMemoryRepository>();
        services.AddHostedService<MemorySeedHostedService>();

        return services;
    }
}