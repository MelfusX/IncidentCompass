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

    [DockerAvailableFact]
    public async Task StartAsync_EditedFileUpdatesSameItemAndReembeds()
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

        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "runbooks", "checkout-timeout.md"),
            """
            ---
            kind: Runbook
            service: checkout-api
            component: payments
            release: 0.2
            tags: [checkout, timeout]
            ---

            # Checkout Timeout Runbook

            Updated guidance uses the circuit-breaker dashboard before retrying payments.
            """,
            TestContext.Current.CancellationToken);

        using (var second = CreateHost(connectionString, sourceDirectory, embeddingClient))
        {
            await second.StartAsync(TestContext.Current.CancellationToken);
            await second.StopAsync(TestContext.Current.CancellationToken);
        }

        var counts = await ReadMemoryCountsAsync(connectionString);
        var state = await ReadSeedStateAsync(connectionString, "runbooks/checkout-timeout.md");
        Assert.Equal(3, embeddingClient.CallCount);
        Assert.Equal(2, counts.Items);
        Assert.Equal(2, counts.Chunks);
        Assert.Equal(2, state.Version);
        Assert.Contains("Updated guidance", state.Content, StringComparison.Ordinal);
        Assert.Equal("0.2", state.ReleaseName);
    }

    [DockerAvailableFact]
    public async Task StartAsync_RemovedFileIsDeactivatedAndStopsContributingChunks()
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

        File.Delete(Path.Combine(sourceDirectory, "incidents", "provider-unavailable.md"));
        using (var second = CreateHost(connectionString, sourceDirectory, embeddingClient))
        {
            await second.StartAsync(TestContext.Current.CancellationToken);
            await second.StopAsync(TestContext.Current.CancellationToken);
        }

        var counts = await ReadMemoryCountsAsync(connectionString);
        var removed = await ReadSeedStateAsync(connectionString, "incidents/provider-unavailable.md");
        Assert.Equal(2, embeddingClient.CallCount);
        Assert.Equal(1, counts.Items);
        Assert.Equal(1, counts.Chunks);
        Assert.False(removed.IsActive);
    }

    [DockerAvailableFact]
    public async Task StartAsync_FrontmatterMetadataIsPersisted()
    {
        var connectionString = await CreateSchemaAsync();
        await ClearMemoryAsync(connectionString);
        var sourceDirectory = await CreateSeedDirectoryAsync();
        using var host = CreateHost(connectionString, sourceDirectory, new CountingEmbeddingClient());

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        var state = await ReadSeedStateAsync(connectionString, "runbooks/checkout-timeout.md");
        Assert.Equal("runbook", state.Kind);
        Assert.Equal("checkout-api", state.ServiceName);
        Assert.Equal("payments", state.Component);
        Assert.Equal("0.1", state.ReleaseName);
        Assert.Contains("checkout", state.Tags);
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
            ---
            kind: Runbook
            service: checkout-api
            component: payments
            release: 0.1
            tags: [checkout, timeout]
            ---

            # Checkout Timeout Runbook

            Checkout timeout alerts usually indicate upstream payment latency.
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "incidents", "provider-unavailable.md"),
            """
            ---
            kind: KnownIncident
            service: provider-client
            component: upstream
            release: 0.1
            tags: [provider, outage]
            ---

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
                (SELECT count(*) FROM incidentcompass.memory_items WHERE tenant_id = 'local' AND is_active = true),
                (SELECT count(*)
                   FROM incidentcompass.memory_chunks chunk
                   JOIN incidentcompass.memory_items item ON item.id = chunk.memory_item_id
                  WHERE chunk.tenant_id = 'local' AND item.is_active = true),
                (SELECT count(DISTINCT source)
                   FROM incidentcompass.memory_items
                  WHERE tenant_id = 'local' AND is_active = true);
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return new MemoryCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<SeedState> ReadSeedStateAsync(string connectionString, string source)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT kind, content, version, service_name, component, release_name, tags, is_active
            FROM incidentcompass.memory_items
            WHERE tenant_id = 'local' AND source = @source AND seed_managed = true
            ORDER BY updated_at_utc DESC
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("source", source);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return new SeedState(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetFieldValue<string[]>(6),
            reader.GetBoolean(7));
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

    private sealed record SeedState(
        string Kind,
        string Content,
        int Version,
        string? ServiceName,
        string? Component,
        string? ReleaseName,
        IReadOnlyList<string> Tags,
        bool IsActive);
}
