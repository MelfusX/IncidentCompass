using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Application.Investigation.Reports.List;
using IncidentCompass.Application.Investigation.Reports.Context;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Embeddings.Mock;
using IncidentCompass.Infrastructure.Embeddings.OpenAi;
using IncidentCompass.Infrastructure.Governance;
using IncidentCompass.Infrastructure.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Intake;
using IncidentCompass.Infrastructure.Investigation;
using IncidentCompass.Infrastructure.Memory;
using IncidentCompass.Infrastructure.ModelGateway.Mock;
using IncidentCompass.Infrastructure.ModelGateway.OpenAi;
using IncidentCompass.Infrastructure.Observability;
using IncidentCompass.Infrastructure.Postgres;
using IncidentCompass.Infrastructure.Security;
using IncidentCompass.Infrastructure.SourceContext;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        services.AddSourceContextInfrastructure(configuration);
        services.AddTicketInfrastructure(configuration);
        // Infrastructure supplies the Worker identity; API auth binds IUserContext explicitly.
        services.TryAddScoped<IBackgroundUserContext, SystemUserContext>();

        return services;
    }

    public static IServiceCollection AddPostgresMigrations(this IServiceCollection services)
    {
        services.TryAddSingleton<IPostgresMigrationFailureInjector, NoPostgresMigrationFailureInjector>();
        services.TryAddSingleton<PostgresMigrationReadiness>();
        services.TryAddSingleton<IPostgresMigrationReadiness>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresMigrationReadiness>());
        services.TryAddSingleton<PostgresMigrationRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PostgresMigrationHostedService>());

        return services;
    }

    private static IServiceCollection AddGovernedInvestigationServices(this IServiceCollection services)
    {
        services.TryAddScoped<TriageLedgerAppender>();
        services.TryAddScoped<InvestigationModelCaller>();
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
        services.TryAddScoped<ITriageLedgerWriter, PostgresTriageLedgerWriter>();
        services.TryAddScoped<ITriageLedgerReader, PostgresTriageLedgerReader>();
        services.TryAddScoped<ITriageJobInvestigationContextRepository, PostgresTriageJobInvestigationContextRepository>();
        services.TryAddScoped<ITriageReportRepository, PostgresTriageReportRepository>();
        services.TryAddScoped<ITriageReportReadRepository, PostgresTriageReportReadRepository>();
        services.TryAddScoped<ITriageReportListRepository, PostgresTriageReportListRepository>();
        services.TryAddScoped<ITriageToolResultCommitter, PostgresTriageToolResultCommitter>();
        services.TryAddScoped<IReadOnlyContextOutcomeRepository, PostgresReadOnlyContextOutcomeRepository>();
        services.TryAddScoped<IActionApprovalTransactionFaultInjector, NoopActionApprovalTransactionFaultInjector>();
        services.TryAddScoped<IActionProposalRepository, PostgresActionProposalRepository>();
        services.TryAddScoped<IActionApprovalReviewRepository, PostgresActionReviewRepository>();
        services.TryAddScoped<IActionDispatchRepository, PostgresActionDispatchRepository>();
        return services;
    }
}
