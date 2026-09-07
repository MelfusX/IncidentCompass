using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Jobs.Testing;
using IncidentCompass.Application.Investigation.Reports.Get;
using IncidentCompass.Application.Investigation.Reports.GetLatest;
using IncidentCompass.Application.Investigation.Reports.List;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Investigation;

internal static class InvestigationSetup
{
    public static IServiceCollection AddInvestigationCore(this IServiceCollection services)
    {
        services.TryAddScoped<ITriageJobRunner, TriageJobRunner>();
        services.TryAddScoped<IClaimedTriageJobProcessor, DeferredClaimedTriageJobProcessor>();
        services.TryAddScoped<IRequestHandler<GetTriageReportQuery, TriageReportDetailsResponse>, GetTriageReportQueryHandler>();
        services.TryAddScoped<IRequestHandler<GetLatestTriageReportQuery, TriageReportDetailsResponse>, GetLatestTriageReportQueryHandler>();
        services.TryAddScoped<IRequestHandler<ListTriageReportsQuery, TriageReportListResponse>, ListTriageReportsQueryHandler>();
        services.AddInvestigationTestFaultSeams();

        return services;
    }

    /// <summary>
    /// Binds the no-op defaults for the <c>Investigation/Jobs/Testing</c> fault seams. These are not
    /// real services: they exist so the integration tests can simulate a crash between two statements
    /// of one commit transaction, which no decorator around the outer port can reach. Production
    /// always gets the no-ops, so this registration is deliberately grouped and named rather than
    /// scattered among the real Investigation services.
    /// </summary>
    private static IServiceCollection AddInvestigationTestFaultSeams(this IServiceCollection services)
    {
        services.TryAddScoped<ITriageToolResultCommitFaultInjector, NoopTriageToolResultCommitFaultInjector>();
        services.TryAddScoped<ITriageReportFinalCommitFaultInjector, NoopTriageReportFinalCommitFaultInjector>();

        return services;
    }
}
