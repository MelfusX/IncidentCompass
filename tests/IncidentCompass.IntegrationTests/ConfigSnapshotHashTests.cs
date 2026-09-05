using IncidentCompass.Application.Intake.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class ConfigSnapshotHashTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task GetCurrentAsync_InstructionEditChangesHashAndOldHashRehydratesPriorSnapshot()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        var configPath = await CreateConfigurationAsync("analysis instructions v1");

        using var firstFactory = CreateFactory(connectionString, configPath);
        var first = await LoadCurrentAsync(firstFactory);

        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(configPath)!, "instructions", "analysis.md"),
            "analysis instructions v2");

        using var secondFactory = CreateFactory(connectionString, configPath);
        var second = await LoadCurrentAsync(secondFactory);
        var old = await LoadByHashAsync(secondFactory, first.ConfigHash);

        Assert.NotEqual(first.ConfigHash, second.ConfigHash);
        Assert.Equal("analysis instructions v1", old.Roles["analysis"].Instructions);
        Assert.Equal("analysis instructions v2", second.Roles["analysis"].Instructions);
    }

    [DockerAvailableFact]
    public async Task GetCurrentAsync_CurrentReleaseEditChangesHashAndOldHashRehydratesPriorSnapshot()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        var configPath = await CreateConfigurationAsync("analysis instructions");

        using var firstFactory = CreateFactory(connectionString, configPath);
        var first = await LoadCurrentAsync(firstFactory);

        var changedConfig = (await File.ReadAllTextAsync(configPath)).Replace(
            "\"checkout\": \"2026.07.13.1\"",
            "\"checkout\": \"2026.07.13.2\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(configPath, changedConfig);

        using var secondFactory = CreateFactory(connectionString, configPath);
        var second = await LoadCurrentAsync(secondFactory);
        var old = await LoadByHashAsync(secondFactory, first.ConfigHash);

        Assert.NotEqual(first.ConfigHash, second.ConfigHash);
        Assert.Equal("2026.07.13.1", old.CurrentReleases["checkout"]);
        Assert.Equal("2026.07.13.2", second.CurrentReleases["checkout"]);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, string configPath)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseExplicitMockProviders();
            builder.UseSetting("IncidentCompass:ConfigSource:Path", configPath);
        });
    }

    private static async Task<TriageConfiguration> LoadCurrentAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITriageConfigurationRepository>();
        return await repository.GetCurrentAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<TriageConfiguration> LoadByHashAsync(WebApplicationFactory<Program> factory, string hash)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITriageConfigurationRepository>();
        return await repository.GetByHashAsync(hash, TestContext.Current.CancellationToken);
    }

    private static async Task<string> CreateConfigurationAsync(string analysisInstructions)
    {
        var directory = Path.Combine(Path.GetTempPath(), "incidentcompass-config-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "instructions"));
        Directory.CreateDirectory(Path.Combine(directory, "schemas"));
        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "orchestrator.md"), "orchestrator instructions");
        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "analysis.md"), analysisInstructions);
        await File.WriteAllTextAsync(Path.Combine(directory, "schemas", "analysis.json"), "{ \"type\": \"object\" }");
        var configPath = Path.Combine(directory, "incidentcompass.config.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "Providers": {
                "local-oai": { "Kind": "Mock" }
              },
              "Routes": {
                "analysis-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "config-hash-model", "Temperature": 0.0, "MaxOutputTokens": 1000, "ContextWindowTokens": 8192 },
                "report-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "config-hash-model", "Temperature": 0.0, "MaxOutputTokens": 1000, "ContextWindowTokens": 8192 }
              },
              "Orchestrator": {
                "Instructions": "ref:instructions/orchestrator.md",
                "RouteId": "report-chat",
                "Tools": ["delegate", "publish_report"],
                "Budget": { "MaxWorkers": 2, "MaxTokens": 100000, "MaxWallClockSeconds": 120, "MaxReprompts": 1 }
              },
              "Roles": {
                "analysis": { "RouteId": "analysis-chat", "Instructions": "ref:instructions/analysis.md", "Tools": [], "OutputSchema": "ref:schemas/analysis.json" }
              },
              "Tools": {},
              "Rules": [],
              "Ingestion": { "DefaultTenant": "local", "AllowedSources": ["otel", "user", "tester", "manual"] },
              "FaultGrouping": {
                "LookbackMinutes": 15,
                "SilenceWindowMinutes": 30,
                "FingerprintVersion": 1,
                "MassIssue": { "MinNeighborCount": 5, "MinFingerprintStrength": "strong" }
              },
              "CurrentReleases": { "checkout": "2026.07.13.1" }
            }
            """);
        return configPath;
    }
}
