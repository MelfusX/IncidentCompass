using IncidentCompass.Infrastructure.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.IntegrationTests;

// Forces the deterministic mock providers for tests that build the default host without
// otherwise touching the model gateway. The product default is OpenAI-compatible, so without
// this these tests would implicitly depend on the real-provider defaults being startup-valid.
public sealed class MockProvidersWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseExplicitMockProviders();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMemorySeedSyncStatusReader>();
            services.AddScoped<IMemorySeedSyncStatusReader, DisabledMemorySeedSyncStatusReader>();
            var migrationService = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType?.Name == "PostgresMigrationHostedService");
            if (migrationService is not null)
            {
                services.Remove(migrationService);
            }
        });
    }

    private sealed class DisabledMemorySeedSyncStatusReader : IMemorySeedSyncStatusReader
    {
        public Task<MemorySeedSyncSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MemorySeedSyncSnapshot(false, false, null, null, null, null));
    }
}
