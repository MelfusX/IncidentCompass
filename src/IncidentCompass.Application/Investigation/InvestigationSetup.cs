using IncidentCompass.Application.Investigation.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Investigation;

internal static class InvestigationSetup
{
    public static IServiceCollection AddInvestigationCore(this IServiceCollection services)
    {
        services.TryAddScoped<ITriageJobRunner, TriageJobRunner>();
        services.TryAddScoped<IClaimedTriageJobProcessor, DeferredClaimedTriageJobProcessor>();

        return services;
    }
}
