using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.Infrastructure.Intake;

internal static class IntakeSetup
{
    public static IServiceCollection AddIntakeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TriageConfigSourceOptions>(configuration.GetSection(TriageConfigSourceOptions.SectionName));

        // Real hosts (WebApplication.CreateBuilder / Host.CreateApplicationBuilder) already
        // register a concrete IHostEnvironment before this method runs, so this TryAddSingleton
        // never overrides it; it only backstops raw ServiceCollection compositions.
        services.TryAddSingleton<IHostEnvironment, FallbackHostEnvironment>();

        services.TryAddSingleton<TriageConfigurationLoadValidator>();
        services.TryAddSingleton<TriageConfigurationMaterializer>();
        services.TryAddSingleton<TriageConfigurationSnapshotStore>();
        services.TryAddSingleton<FileTriageConfigurationRepository>();
        services.TryAddSingleton<ITriageConfigurationRepository>(
            serviceProvider => serviceProvider.GetRequiredService<FileTriageConfigurationRepository>());

        services.TryAddScoped<PostgresIntakeTransactionContext>();
        services.TryAddScoped<IIntakeUnitOfWork, PostgresIntakeUnitOfWork>();
        services.TryAddScoped<ISignalRepository, PostgresSignalRepository>();
        services.TryAddScoped<IFaultRepository, PostgresFaultRepository>();
        services.TryAddScoped<ITriageJobRepository, PostgresTriageJobRepository>();
        services.TryAddScoped<ITriageJobRuntimeRepository, PostgresTriageJobRuntimeRepository>();
        services.TryAddScoped<ITriageArtifactRepository, PostgresTriageArtifactRepository>();
        services.TryAddScoped<IPriorReportSummaryProvider, PostgresPriorReportSummaryProvider>();

        services.AddHostedService<TriageConfigurationWarmupHostedService>();

        return services;
    }
}

