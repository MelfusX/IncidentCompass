using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Configuration;

internal static class PostgresConnectionOptionsSetup
{
    /// <summary>
    /// Registers the two-step PostgreSQL connection binding in one place:
    /// <see cref="PostgresOptions"/> names the connection string and
    /// <see cref="PostgresConnectionOptions"/> carries the resolved
    /// <c>ConnectionStrings:&lt;name&gt;</c> value. The <see cref="IConfiguration"/> is captured
    /// here instead of being registered as a service, so adapters depend on typed options only.
    /// </summary>
    public static IServiceCollection AddPostgresConnectionOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<PostgresConnectionOptions>,
            PostgresConnectionOptionsConfigurator>(
            serviceProvider => new PostgresConnectionOptionsConfigurator(
                configuration,
                serviceProvider.GetRequiredService<IOptions<PostgresOptions>>())));

        return services;
    }
}
