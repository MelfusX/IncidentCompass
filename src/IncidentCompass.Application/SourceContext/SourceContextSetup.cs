using IncidentCompass.Application.Governance.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.SourceContext;

internal static class SourceContextSetup
{
    public static IServiceCollection AddSourceContextCore(this IServiceCollection services)
    {
        services.TryAddScoped<ISourceContextLookup, UnavailableSourceContextLookup>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IImmediateAgentTool, SourceLookupTool>());
        services.AddSingleton(new AgentToolDescriptor("source_lookup", AgentToolCapability.ImmediateRead));
        return services;
    }
}
