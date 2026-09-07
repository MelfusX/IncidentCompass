using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Application.Investigation.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Application.Core.Composition;

/// <summary>
/// Fails host composition when a port that only an infrastructure adapter can implement is still
/// bound to its deferred "not configured" placeholder. <c>AddApplication</c> registers those
/// placeholders so partial graphs (unit tests, per-feature composition) stay buildable;
/// <c>AddInfrastructure</c> replaces them. Without this check a host that forgets
/// <c>AddInfrastructure</c> starts cleanly and only throws at the first claimed triage job or the
/// first recurrence escalation, in production. Hosts call it last, after every registration, so it
/// sees the descriptors the container will actually resolve.
/// </summary>
public static class ApplicationWiringValidator
{
    public static IServiceCollection ValidateApplicationWiring(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        RequireConfiguredAdapter<IClaimedTriageJobProcessor, DeferredClaimedTriageJobProcessor>(services);
        RequireConfiguredAdapter<IRecurrenceEscalationReTriageScheduler, DeferredRecurrenceEscalationReTriageScheduler>(
            services);

        return services;
    }

    private static void RequireConfiguredAdapter<TService, TDeferred>(IServiceCollection services)
        where TDeferred : TService
    {
        // Last registration wins in the container, so the last descriptor is the effective one.
        var descriptor = services.LastOrDefault(
            candidate => candidate.ServiceType == typeof(TService));

        if (descriptor is null)
        {
            throw new InvalidOperationException(
                $"No implementation of '{typeof(TService).Name}' is registered. " +
                "Compose AddApplication and AddInfrastructure before AddApi or AddWorker.");
        }

        if (descriptor.ImplementationType == typeof(TDeferred))
        {
            throw new InvalidOperationException(
                $"'{typeof(TService).Name}' is still bound to the deferred placeholder " +
                $"'{typeof(TDeferred).Name}', which throws when it is called. " +
                "Compose AddInfrastructure before AddApi or AddWorker so a real adapter is registered.");
        }
    }
}
