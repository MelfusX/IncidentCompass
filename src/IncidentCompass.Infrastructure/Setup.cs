using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Infrastructure.Investigation;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Governance;
using IncidentCompass.Infrastructure.Intake;
using IncidentCompass.Infrastructure.Memory;
using IncidentCompass.Infrastructure.Observability;
using IncidentCompass.Infrastructure.Postgres;
using IncidentCompass.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IncidentCompass.Infrastructure.ModelGateway.Mock;
using IncidentCompass.Infrastructure.ModelGateway.OpenAi;
using IncidentCompass.Infrastructure.Embeddings.Mock;
using IncidentCompass.Infrastructure.Embeddings.OpenAi;

namespace IncidentCompass.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Raw ServiceCollection-based tests and non-host composition still need IConfiguration
        // for connection-string resolution in PostgreSQL adapters.
        services.TryAddSingleton(configuration);
        services.AddInfrastructureOptions(configuration);
        services.AddModelGatewayAdapters();
        services.AddEmbeddingAdapters();
        services.AddGovernedInvestigationServices();
        services.Replace(ServiceDescriptor.Scoped<IClaimedTriageJobProcessor, GovernedTriageInvestigationProcessor>());
        services.AddObservabilityInfrastructure(configuration);
        services.AddPersistenceAdapters();
        services.AddIntakeInfrastructure(configuration);
        services.AddMemoryInfrastructure(configuration);
        // Infrastructure supplies the background identity used by Worker hosts.
        // API foreground auth must bind IUserContext explicitly.
        services.TryAddScoped<IBackgroundUserContext, SystemUserContext>();

        return services;
    }


    private static IServiceCollection AddGovernedInvestigationServices(this IServiceCollection services)
    {
        services.TryAddScoped<TriageLedgerAppender>();
        services.TryAddScoped<InvestigationModelCaller>();
        services.TryAddScoped<WorkerToolRuleEngine>();
        services.TryAddScoped<WorkerToolCallExecutor>();
        services.TryAddScoped<WorkerRoleRunner>();
        services.TryAddScoped<AnalysisDelegateExecutor>();
        services.TryAddScoped<TriageReportPublisher>();

        return services;
    }
    private static IServiceCollection AddInfrastructureOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        services
            .AddOptions<ModelGatewayOptions>()
            .Bind(configuration.GetSection(ModelGatewayOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<ModelGatewayOptions>,
            ModelGatewayProviderOptionsValidator>());
        services
            .AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<EmbeddingOptions>,
            EmbeddingProviderOptionsValidator>());
        services
            .AddOptions<OpenAiCompatibleModelClientOptions>()
            .Bind(configuration.GetSection(OpenAiCompatibleModelClientOptions.SectionName))
            .Validate<IOptions<ModelGatewayOptions>>(
                static (openAiOptions, modelGatewayOptions) =>
                    !ProviderKindParser.IsOpenAiCompatible(modelGatewayOptions.Value.Provider) ||
                    openAiOptions.IsValid(),
                "OpenAI-compatible model gateway configuration is invalid.")
            .ValidateOnStart();
        services
            .AddOptions<OpenAiCompatibleEmbeddingClientOptions>()
            .Bind(configuration.GetSection(OpenAiCompatibleEmbeddingClientOptions.SectionName))
            .Validate<IOptions<EmbeddingOptions>>(
                static (openAiOptions, embeddingOptions) =>
                    !ProviderKindParser.IsOpenAiCompatible(embeddingOptions.Value.Provider) ||
                    openAiOptions.IsValid(),
                "OpenAI-compatible embedding provider configuration is invalid.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddModelGatewayAdapters(this IServiceCollection services)
    {
        services.AddHttpClient<OpenAiCompatibleModelClient>();

        services.TryAddScoped<MockAiModelClient>();
        services.TryAddScoped<IAiModelClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<ModelGatewayOptions>>()
                .Value;

            if (!ProviderKindParser.TryParse(options.Provider, out var providerKind))
            {
                throw new InvalidOperationException(
                    $"Unsupported model gateway provider '{options.Provider}'.");
            }

            return providerKind switch
            {
                ProviderKind.Mock => serviceProvider.GetRequiredService<MockAiModelClient>(),
                ProviderKind.OpenAiCompatible => serviceProvider.GetRequiredService<OpenAiCompatibleModelClient>(),
                _ => throw new InvalidOperationException(
                    $"Unsupported model gateway provider '{options.Provider}'.")
            };
        });

        return services;
    }

    private static IServiceCollection AddEmbeddingAdapters(this IServiceCollection services)
    {
        services.AddHttpClient<OpenAiCompatibleEmbeddingClient>();

        services.TryAddScoped<MockEmbeddingClient>();
        services.TryAddScoped<IEmbeddingClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<EmbeddingOptions>>()
                .Value;

            if (!ProviderKindParser.TryParse(options.Provider, out var providerKind))
            {
                throw new InvalidOperationException(
                    $"Unsupported embedding provider '{options.Provider}'.");
            }

            return providerKind switch
            {
                ProviderKind.Mock => serviceProvider.GetRequiredService<MockEmbeddingClient>(),
                ProviderKind.OpenAiCompatible => serviceProvider.GetRequiredService<OpenAiCompatibleEmbeddingClient>(),
                _ => throw new InvalidOperationException(
                    $"Unsupported embedding provider '{options.Provider}'.")
            };
        });

        return services;
    }

    private static IServiceCollection AddPersistenceAdapters(this IServiceCollection services)
    {
        services.TryAddSingleton<PostgresDataSourceProvider>();
        services.TryAddScoped<PostgresObservabilityRepository>();
        services.TryAddScoped<IAiRequestLogRepository>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresObservabilityRepository>());
        services.TryAddScoped<IPricingRepository>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresObservabilityRepository>());
        services.TryAddScoped<IToolAuditLogRepository, PostgresToolAuditLogRepository>();
        services.TryAddScoped<ITriageLedgerWriter, PostgresTriageLedgerWriter>();
        services.TryAddScoped<ITriageLedgerReader, PostgresTriageLedgerReader>();
        services.TryAddScoped<ITriageJobInvestigationContextRepository, PostgresTriageJobInvestigationContextRepository>();
        services.TryAddScoped<ITriageReportRepository, PostgresTriageReportRepository>();
        services.TryAddScoped<ITriageToolResultCommitter, PostgresTriageToolResultCommitter>();

        return services;
    }
}



