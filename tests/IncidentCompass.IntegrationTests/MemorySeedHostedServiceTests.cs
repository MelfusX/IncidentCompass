using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class MemorySeedHostedServiceTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task StartAsync_TwoHostsSeedSameDatabaseConcurrently_SeedsCorpusOnce()
    {
        var connectionString = await CreateSchemaAsync();
        await ClearMemoryAsync(connectionString);
        var sourceDirectory = await CreateSeedDirectoryAsync();
        var firstEmbeddingClient = new CountingEmbeddingClient();
        var secondEmbeddingClient = new CountingEmbeddingClient();
        using var first = CreateHost(connectionString, sourceDirectory, firstEmbeddingClient);
        using var second = CreateHost(connectionString, sourceDirectory, secondEmbeddingClient);

        await Task.WhenAll(
            first.StartAsync(TestContext.Current.CancellationToken),
            second.StartAsync(TestContext.Current.CancellationToken));

        await Task.WhenAll(
            first.StopAsync(TestContext.Current.CancellationToken),
            second.StopAsync(TestContext.Current.CancellationToken));
        var counts = await ReadMemoryCountsAsync(connectionString);

        Assert.Equal(2, counts.Items);
        Assert.Equal(2, counts.Chunks);
        Assert.Equal(2, counts.Sources);
    }

    [DockerAvailableFact]
    public async Task StartAsync_AlreadySeededDatabase_SkipsExistingContentHashWithoutEmbedding()
    {
        var connectionString = await CreateSchemaAsync();
        await ClearMemoryAsync(connectionString);
        var sourceDirectory = await CreateSeedDirectoryAsync();
        var embeddingClient = new CountingEmbeddingClient();
        using (var first = CreateHost(connectionString, sourceDirectory, embeddingClient))
        {
            await first.StartAsync(TestContext.Current.CancellationToken);
            await first.StopAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, embeddingClient.CallCount);

        using (var second = CreateHost(connectionString, sourceDirectory, embeddingClient))
        {
            await second.StartAsync(TestContext.Current.CancellationToken);
            await second.StopAsync(TestContext.Current.CancellationToken);
        }

        var counts = await ReadMemoryCountsAsync(connectionString);
        Assert.Equal(2, embeddingClient.CallCount);
        Assert.Equal(2, counts.Items);
        Assert.Equal(2, counts.Chunks);
        Assert.Equal(2, counts.Sources);
    }

    private async Task<string> CreateSchemaAsync()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        return connectionString;
    }

    private static IHost CreateHost(
        string connectionString,
        string sourceDirectory,
        CountingEmbeddingClient embeddingClient)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IncidentCompass"] = connectionString,
                    ["IncidentCompass:ConfigSource:Path"] = Path.Combine(FindRepositoryRoot(), "config", "incidentcompass.config.json"),
                    ["IncidentCompass:Memory:Seed:Enabled"] = "true",
                    ["IncidentCompass:Memory:Seed:TenantId"] = "local",
                    ["IncidentCompass:Memory:Seed:SourceDirectory"] = sourceDirectory
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddSingleton<IEmbeddingClient>(embeddingClient);
                services.AddTestApplication(context.Configuration);
                services.AddInfrastructure(context.Configuration);
            })
            .Build();
    }

    private static async Task<string> CreateSeedDirectoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "incidentcompass-memory-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "runbooks"));
        Directory.CreateDirectory(Path.Combine(directory, "incidents"));
        await File.WriteAllTextAsync(
            Path.Combine(directory, "runbooks", "checkout-timeout.md"),
            """
            # Checkout Timeout Runbook

            Checkout timeout alerts usually indicate upstream payment latency.
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "incidents", "provider-unavailable.md"),
            """
            # Provider Unavailable Incident

            ProviderUnavailableException bursts usually point to dependency outage.
            """,
            TestContext.Current.CancellationToken);
        return directory;
    }

    private static async Task ClearMemoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM incidentcompass.memory_items;", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<MemoryCounts> ReadMemoryCountsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT count(*) FROM incidentcompass.memory_items WHERE tenant_id = 'local'),
                (SELECT count(*) FROM incidentcompass.memory_chunks WHERE tenant_id = 'local'),
                (SELECT count(DISTINCT source) FROM incidentcompass.memory_items WHERE tenant_id = 'local');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return new MemoryCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class CountingEmbeddingClient : IEmbeddingClient
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public Task<EmbeddingResponse> CreateEmbeddingAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new EmbeddingResponse([1f, 0f], request.Model, "test", 1, request.CorrelationId));
        }
    }

    private sealed record MemoryCounts(long Items, long Chunks, long Sources);
}
