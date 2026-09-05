using IncidentCompass.Application.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

public static class Setup
{
    public static IServiceCollection AddWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<WorkerOptions>,
            WorkerOptionsValidator>());
        services
            .AddOptions<ActionDispatchOptions>()
            .Bind(configuration.GetSection(ActionDispatchOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ActionDispatchOptions>,
            ActionDispatchOptionsValidator>());

        services.AddScoped<IUserContext>(
            serviceProvider => serviceProvider.GetRequiredService<IBackgroundUserContext>());
        services.TryAddSingleton<WorkerJobLeaseRenewer>();
        services.TryAddSingleton<WorkerJobPump>();
        services.TryAddSingleton<WorkerActionPump>();
        services.AddHostedService<Worker>();
        services.AddHostedService<ActionDispatchWorker>();

        return services;
    }
}
