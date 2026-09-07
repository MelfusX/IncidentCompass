using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.IntegrationTests;

public sealed class ApiKeyOpenApiContractTests
{
    [Fact]
    public async Task DocumentDefinesHeaderSchemeAndRequirementsOnlyForProtectedOperations()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseExplicitMockProviders();
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("IncidentCompassApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-IncidentCompass-Key", scheme.GetProperty("name").GetString());

        var protectedOperation = root.GetProperty("paths").GetProperty("/api/v1/incidents").GetProperty("post");
        Assert.Equal(
            0,
            protectedOperation.GetProperty("security")[0]
                .GetProperty("IncidentCompassApiKey")
                .GetArrayLength());
        Assert.True(protectedOperation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(protectedOperation.GetProperty("responses").TryGetProperty("429", out _));

        var anonymousOperation = root.GetProperty("paths").GetProperty("/api/v1/health").GetProperty("get");
        Assert.False(anonymousOperation.TryGetProperty("security", out _));
        Assert.False(anonymousOperation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.False(anonymousOperation.GetProperty("responses").TryGetProperty("429", out _));

        foreach (var route in new[] { "/v1/traces", "/v1/logs" })
        {
            var otlpResponses = root.GetProperty("paths").GetProperty(route).GetProperty("post").GetProperty("responses");
            Assert.True(otlpResponses.TryGetProperty("200", out _));
            Assert.True(otlpResponses.TryGetProperty("401", out _));
            Assert.True(otlpResponses.TryGetProperty("429", out _));
        }
    }
}
