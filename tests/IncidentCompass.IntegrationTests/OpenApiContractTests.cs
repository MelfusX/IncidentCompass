using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.IntegrationTests;

public sealed class OpenApiContractTests
{
    private static readonly JsonSerializerOptions BaselineJsonOptions = new()
    {
        WriteIndented = true
    };

    [Fact]
    public async Task DevelopmentOpenApiDocument_MatchesCommittedBaseline()
    {
        using var developmentFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseExplicitMockProviders();
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        });
        using var client = developmentFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var actual = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var normalizedActual = NormalizeJson(actual);
        if (IsTruthy(Environment.GetEnvironmentVariable("INCIDENTCOMPASS_UPDATE_OPENAPI_BASELINE")))
        {
            await File.WriteAllTextAsync(BaselinePath(), normalizedActual + Environment.NewLine, TestContext.Current.CancellationToken);
        }

        var expected = await File.ReadAllTextAsync(BaselinePath(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(NormalizeJson(expected), normalizedActual);
    }

    private static string BaselinePath([CallerFilePath] string sourceFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "Baselines", "openapi-v1.json");

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, BaselineJsonOptions);
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
            (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
