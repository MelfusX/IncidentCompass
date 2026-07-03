using IncidentCompass.Application.Governance.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Memory;

internal static class MemorySetup
{
    public static IServiceCollection AddMemoryCore(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, MemorySearchTool>());

        return services;
    }
}
