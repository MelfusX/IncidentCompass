using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports.Get;
using IncidentCompass.Application.Investigation.Reports.GetLatest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Investigation;

internal static class InvestigationSetup
{
    public static IServiceCollection AddInvestigationCore(this IServiceCollection services)
    {
        services.TryAddScoped<ITriageJobRunner, TriageJobRunner>();
        services.TryAddScoped<IClaimedTriageJobProcessor, DeferredClaimedTriageJobProcessor>();
        services.TryAddScoped<ITriageToolResultCommitFaultInjector, NoopTriageToolResultCommitFaultInjector>();
        services.TryAddScoped<ITriageReportFinalCommitFaultInjector, NoopTriageReportFinalCommitFaultInjector>();
        services.TryAddScoped<IRequestHandler<GetTriageReportQuery, TriageReportDetailsResponse>, GetTriageReportQueryHandler>();
        services.TryAddScoped<IRequestHandler<GetLatestTriageReportQuery, TriageReportDetailsResponse>, GetLatestTriageReportQueryHandler>();

        return services;
    }
}
