using IncidentCompass.Application;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Governance;
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
}
