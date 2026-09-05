using IncidentCompass.Application.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class TicketInfrastructureSetup
{
    public static IServiceCollection AddTicketInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GitHubIssuesOptions>()
            .Bind(configuration.GetSection(GitHubIssuesOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<GitHubIssuesOptions>,
            GitHubIssuesOptionsValidator>());
        services.AddHttpClient<GitHubIssuesTicketSearch>(client =>
            {
                client.BaseAddress = GitHubIssuesTicketSearch.Authority;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(GitHubIssuesHttpMessageHandlerFactory.Create);
        services.Replace(ServiceDescriptor.Scoped<ITicketSearch>(provider =>
            provider.GetRequiredService<GitHubIssuesTicketSearch>()));
        return services;
    }
}
