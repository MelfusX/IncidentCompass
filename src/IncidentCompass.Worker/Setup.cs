using IncidentCompass.Application.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Worker;

public static class Setup
{
    public static IServiceCollection AddWorker(this IServiceCollection services)
    {
        services.AddScoped<IUserContext>(
            serviceProvider => serviceProvider.GetRequiredService<IBackgroundUserContext>());
        services.AddHostedService<Worker>();

        return services;
    }
}
