using FluentValidation;
using IncidentCompass.Application.Governance.Tools.Execution;
using IncidentCompass.Application.Core.Configuration;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Core.Health;
using IncidentCompass.Application.Core.Users;
using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Application.Intake;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Governance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Application;

public static class Setup
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApplicationOptions(configuration);
        services.AddValidatorsFromAssembly(typeof(Setup).Assembly, includeInternalTypes: true);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IApplicationDispatcher, ApplicationDispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(DispatchLoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RequestValidationBehavior<,>));
        services.AddHealthCore();
        services.AddUsersCore();
        services.AddIntakeCore();

        services.TryAddScoped<ModelGatewayRequestPolicy>();
        services.TryAddScoped<IAiModelRequestLogger, NoopAiModelRequestLogger>();

        services.TryAddScoped<ToolPolicy>();
        services.TryAddScoped<AgentToolAuditLogWriter>();
        services.TryAddScoped<GovernedAgentToolExecutor>();

        return services;
    }

    private static IServiceCollection AddApplicationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ApplicationOptions>,
            ApplicationOptionsValidator>());

        services
            .AddOptions<ModelGatewayOptions>()
            .Bind(configuration.GetSection(ModelGatewayOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ModelGatewayOptions>,
            ModelGatewayOptionsValidator>());

        services
            .AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<EmbeddingOptions>,
            EmbeddingOptionsValidator>());

        services
            .AddOptions<IngestionLimitsOptions>()
            .Bind(configuration.GetSection(IngestionLimitsOptions.SectionName));

        return services;
    }
}
