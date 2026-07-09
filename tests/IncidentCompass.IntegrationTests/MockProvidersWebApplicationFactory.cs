using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IncidentCompass.IntegrationTests;

// Forces the deterministic mock providers for tests that build the default host without
// otherwise touching the model gateway. The product default is OpenAI-compatible, so without
// this these tests would implicitly depend on the real-provider defaults being startup-valid.
public sealed class MockProvidersWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseExplicitMockProviders();
    }
}
