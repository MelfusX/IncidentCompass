using IncidentCompass.Application;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Governance;
using IncidentCompass.Domain.Incidents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

public sealed class MemoryOnlyCompositionTests
{
    [Fact]
    public void Application_CanBuildWithoutChatModelServices()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(configuration);
        services.AddSingleton<MemoryOnlyUserContext>();
        services.AddSingleton<IUserContext>(serviceProvider =>
            serviceProvider.GetRequiredService<MemoryOnlyUserContext>());
        services.AddSingleton<IBackgroundUserContext>(serviceProvider =>
            serviceProvider.GetRequiredService<MemoryOnlyUserContext>());
        // The Governance subsystem (kept as currently-uncalled library code) depends on
        // IToolAuditLogRepository, which is an Infrastructure-provided port. This test
        // composes Application in isolation, so it supplies an in-memory stand-in instead
        // of pulling in IncidentCompass.Infrastructure.
        services.AddSingleton<IToolAuditLogRepository, InMemoryToolAuditLogRepository>();
        // IngestSignalCommandValidator (Phase 1 intake) depends on ITriageConfigurationRepository,
        // another Infrastructure-provided port. Same reasoning as above: supply a trivial
        // in-memory stand-in instead of pulling in IncidentCompass.Infrastructure.
        services.AddSingleton<ITriageConfigurationRepository, InMemoryTriageConfigurationRepository>();
        // FaultGroupingCoordinator/GroundedFactsAssembler/GetFaultQueryHandler (Phase 1 intake)
        // depend on these repository ports, all Infrastructure-provided. Same reasoning as above:
        // supply trivial in-memory stand-ins instead of pulling in IncidentCompass.Infrastructure.
        services.AddSingleton<ISignalRepository, InMemorySignalRepository>();
        services.AddSingleton<IFaultRepository, InMemoryFaultRepository>();
        services.AddSingleton<IIntakeUnitOfWork, InMemoryIntakeUnitOfWork>();
        services.AddSingleton<ITriageJobRepository, InMemoryTriageJobRepository>();
        services.AddSingleton<ITriageArtifactRepository, InMemoryTriageArtifactRepository>();
        services.AddSingleton<IPriorReportSummaryProvider, InMemoryPriorReportSummaryProvider>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>());
        Assert.Empty(scope.ServiceProvider.GetServices<IAiModelClient>());
    }

    private sealed class MemoryOnlyUserContext : IBackgroundUserContext
    {
        public bool IsAuthenticated => true;

        public string? UserId => "system";

        public string? TenantId => null;

        public IReadOnlyCollection<string> Roles => ["system"];

        public IReadOnlyCollection<string> Groups => [];
    }

    private sealed class InMemoryToolAuditLogRepository : IToolAuditLogRepository
    {
        public Task AddAsync(ToolAuditLogEntry entry, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTriageConfigurationRepository : ITriageConfigurationRepository
    {
        private static readonly TriageConfiguration Configuration = new(
            ConfigHash: "memory-only-test-hash",
            Providers: new Dictionary<string, TriageProviderSettings>(StringComparer.Ordinal)
            {
                ["local-oai"] = new("OpenAICompatible", "http://localhost:1234/v1", "LOCAL_OAI_KEY")
            },
            Routes: new Dictionary<string, TriageRouteSettings>(StringComparer.Ordinal)
            {
                ["analysis-chat"] = new("Chat", "local-oai", "local-model", 0.1, 2000, 8192),
                ["report-chat"] = new("Chat", "local-oai", "local-model", 0.2, 4000, 8192),
                ["memory-embed"] = new("Embedding", "local-oai", "local-embedding-model", null, null, null)
            },
            Orchestrator: new OrchestratorSettings(
                "orchestrator instructions",
                "report-chat",
                ["delegate", "publish_report"],
                new OrchestratorBudgetSettings(6, 200000, 120)),
            Roles: new Dictionary<string, TriageRoleSettings>(StringComparer.Ordinal)
            {
                ["analysis"] = new("analysis-chat", "analysis instructions", [], "analysis schema")
            },
            Tools: new Dictionary<string, TriageToolSettings>(StringComparer.Ordinal),
            Rules: [],
            Ingestion: new IngestionSettings(DefaultTenant: "local", AllowedSources: ["tester"]),
            FaultGrouping: new FaultGroupingSettings(
                LookbackMinutes: 15,
                SilenceWindowMinutes: 30,
                FingerprintVersion: 1,
                MassIssue: new MassIssueSettings(MinNeighborCount: 5, MinFingerprintStrength: "strong")));

        public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Configuration);

        public Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken) =>
            Task.FromResult(Configuration);
    }

    private sealed class InMemoryIntakeUnitOfWork : IIntakeUnitOfWork
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }
    private sealed class InMemorySignalRepository : ISignalRepository
    {
        public Task InsertAsync(Signal signal, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AttachToFaultAsync(Guid signalId, Guid faultId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CountDistinctNeighborsAsync(
            string tenantId,
            string serviceName,
            string environment,
            string fingerprint,
            int fingerprintVersion,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class InMemoryFaultRepository : IFaultRepository
    {
        public Task<Fault?> FindOpenFaultAsync(
            string tenantId,
            string serviceName,
            string environment,
            string fingerprint,
            int fingerprintVersion,
            CancellationToken cancellationToken) => Task.FromResult<Fault?>(null);

        public Task<Fault?> FindMostRecentClosedFaultAsync(
            string tenantId,
            string serviceName,
            string environment,
            string fingerprint,
            int fingerprintVersion,
            CancellationToken cancellationToken) => Task.FromResult<Fault?>(null);

        public Task<Fault?> TryInsertAsync(Fault fault, CancellationToken cancellationToken) => Task.FromResult<Fault?>(fault);

        public Task<Fault?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Fault?>(null);
    }

    private sealed class InMemoryTriageJobRepository : ITriageJobRepository
    {
        public Task<TriageJob> InsertPendingAsync(Guid faultId, string configHash, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new TriageJob(
                Id: Guid.NewGuid(),
                FaultId: faultId,
                Status: TriageJobStatus.Pending,
                Attempt: 1,
                LockedBy: null,
                LockedUntilUtc: null,
                NextAttemptAtUtc: null,
                LastErrorCode: null,
                LastErrorMessage: null,
                ConfigHash: configHash,
                CreatedAtUtc: now,
                UpdatedAtUtc: now));
        }

        public Task<TriageJob?> FindByFaultIdAsync(Guid faultId, CancellationToken cancellationToken) => Task.FromResult<TriageJob?>(null);
    }

    private sealed class InMemoryTriageArtifactRepository : ITriageArtifactRepository
    {
        public Task InsertAsync(TriageArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryPriorReportSummaryProvider : IPriorReportSummaryProvider
    {
        public Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken) =>
            Task.FromResult<PriorReportSummary?>(null);
    }
}
