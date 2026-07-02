using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Application.Intake.GetFault;
using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Application.Intake.Normalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Intake;

internal static class IntakeSetup
{
    public static IServiceCollection AddIntakeCore(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalNormalizer, TesterSignalNormalizer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalNormalizer, OtelShapedSignalNormalizer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalNormalizer, UserReportSignalNormalizer>());
        services.TryAddSingleton<SignalNormalizerRegistry>();

        services.TryAddScoped<GroundedFactsAssembler>();
        services.TryAddScoped<FaultGroupingCoordinator>();

        services.TryAddScoped<IRequestHandler<IngestSignalCommand, IngestSignalResponse>, IngestSignalCommandHandler>();
        services.TryAddScoped<IRequestHandler<GetFaultQuery, FaultDetailsResponse>, GetFaultQueryHandler>();

        return services;
    }
}
