using IncidentCompass.Application.SourceContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.SourceContext;

internal static class SourceContextInfrastructureSetup
{
    public static IServiceCollection AddSourceContextInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SourceContextOptions>()
            .Bind(configuration.GetSection(SourceContextOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<SourceContextOptions>,
            SourceContextOptionsValidator>());
        services.Replace(ServiceDescriptor.Scoped<ISourceContextLookup, LocalSourceContextLookup>());
        return services;
    }
}
