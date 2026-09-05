using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Notifications.Telegram;
using IncidentCompass.Infrastructure.Tickets;
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
        services
            .AddOptions<PostReportActionEvaluationOptions>()
            .Bind(configuration.GetSection(PostReportActionEvaluationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PostReportActionEvaluationOptions>,
            PostReportActionEvaluationOptionsValidator>());
        services
            .AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TelegramOptions>,
            TelegramOptionsValidator>());

        services.AddSingleton<IPostReportActionWorkflow, TelegramNotificationWorkflow>();
        services.AddSingleton<IPostReportActionWorkflow, TicketCreatePostReportActionWorkflow>();
        services.AddSingleton<TelegramNotificationActionTool>();
        services.AddSingleton<IExternalActionTool>(
            serviceProvider => serviceProvider.GetRequiredService<TelegramNotificationActionTool>());
        services.AddScoped<GitHubIssuesTicketCreate>();
        services.AddScoped<IExternalActionTool>(
            serviceProvider => serviceProvider.GetRequiredService<GitHubIssuesTicketCreate>());

        services.AddScoped<IUserContext>(
            serviceProvider => serviceProvider.GetRequiredService<IBackgroundUserContext>());
        services.TryAddSingleton<WorkerJobLeaseRenewer>();
        services.TryAddSingleton<WorkerJobPump>();
        services.TryAddSingleton<WorkerActionPump>();
        services.TryAddSingleton<PostReportActionEvaluationLeaseRenewer>();
        services.TryAddSingleton<PostReportActionEvaluationPump>();
        services.AddHostedService<TelegramConfigurationStartupValidator>();
        services.AddHostedService<GitHubIssueConfigurationStartupValidator>();
        services.AddHostedService<Worker>();
        services.AddHostedService<ActionDispatchWorker>();
        services.AddHostedService<PostReportActionEvaluationWorker>();

        return services;
    }
}
