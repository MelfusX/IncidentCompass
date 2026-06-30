using IncidentCompass.Application.Generation.ModelGateway;
using IncidentCompass.Infrastructure.Observability.Logging;
using IncidentCompass.Infrastructure.Observability.Pricing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Observability;

public static class ObservabilitySetup
{
    public static IServiceCollection AddObservabilityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddObservabilityOptions(configuration);
        services.TryAddScoped<AiCostEstimator>();
        services.TryAddScoped<AiRequestLogWriter>();
        services.TryAddScoped<AiModelRequestLoggingService>();
        services.Replace(ServiceDescriptor.Scoped<IAiModelRequestLogger, AiModelRequestLogger>());

        return services;
    }

    private static IServiceCollection AddObservabilityOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiRequestLoggingOptions>()
            .Bind(configuration.GetSection(AiRequestLoggingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AiRequestLoggingOptions>,
            AiRequestLoggingOptionsValidator>());
        return services;
    }
}
