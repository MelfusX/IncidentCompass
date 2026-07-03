using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Application.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class MemorySearchTests(PostgresRepositoryFixture postgres)
{
    private const string MemoryModel = "mock-memory-embedding-v1";

    [DockerAvailableFact]
    public async Task MemoryChunks_RejectEmbeddingDimensionMismatch()
    {
        var connectionString = await CreateSchemaAsync();
        await ClearMemoryAsync(connectionString);
        var itemId = await InsertMemoryItemAsync(connectionString, "local", "dimension-mismatch.md");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertMemoryChunkAsync(
                connectionString,
                itemId,
                "local",
                "mock",
                MemoryModel,
                embeddingDimensions: 3,
                [1f, 0f],
                TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [DockerAvailableFact]
    public async Task SearchAsync_UsesExactTenantProviderModelDimensionFilters()
    {
        using var scope = await CreateScopeAsync();
        var itemId = await InsertMemoryItemAsync(scope.ConnectionString, "local", "exact-filter.md");
        await InsertMemoryChunkAsync(
            scope.ConnectionString,
            itemId,
            "local",
            "mock",
            MemoryModel,
            embeddingDimensions: 2,
            [1f, 0f],
            TestContext.Current.CancellationToken);

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<IMemoryRepository>();

        var match = await repository.SearchAsync(
            new MemorySearchRequest("local", "mock", MemoryModel, 2, [1f, 0f], TopK: 5, MinScore: 0.25),
            TestContext.Current.CancellationToken);
        var dimensionMismatch = await repository.SearchAsync(
            new MemorySearchRequest("local", "mock", MemoryModel, 3, [1f, 0f, 0f], TopK: 5, MinScore: 0.25),
            TestContext.Current.CancellationToken);
        var modelMismatch = await repository.SearchAsync(
            new MemorySearchRequest("local", "mock", "other-model", 2, [1f, 0f], TopK: 5, MinScore: 0.25),
            TestContext.Current.CancellationToken);

        Assert.Single(match);
        Assert.Empty(dimensionMismatch);
        Assert.Empty(modelMismatch);
    }

    private async Task<TestScope> CreateScopeAsync()
    {
        var connectionString = await CreateSchemaAsync();
        await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(connectionString);
        await ClearMemoryAsync(connectionString);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        return new TestScope(factory, client, connectionString);
    }

    private async Task<string> CreateSchemaAsync()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        return connectionString;
    }

    private static async Task<Guid> InsertMemoryItemAsync(string connectionString, string tenantId, string source)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash, version, tags, created_at_utc)
            VALUES (
                @id, @tenant_id, 'runbook', @source, 'Exact filter test', 'checkout timeout inventory',
                @content_hash, 1, ARRAY['test']::text[], @created_at_utc);
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("content_hash", Hash(source));
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task InsertMemoryChunkAsync(
        string connectionString,
        Guid itemId,
        string tenantId,
        string provider,
        string model,
        int embeddingDimensions,
        float[] values,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_chunks (
                id, memory_item_id, tenant_id, chunk_position, text, text_hash,
                embedding_provider, embedding_model, embedding_dimensions,
                embedding_values, embedding_vector, created_at_utc)
            VALUES (
                @id, @memory_item_id, @tenant_id, 0, 'checkout timeout inventory', @text_hash,
                @embedding_provider, @embedding_model, @embedding_dimensions,
                @embedding_values, @embedding_vector::vector, @created_at_utc);
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("memory_item_id", itemId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("text_hash", Hash(model + embeddingDimensions));
        command.Parameters.AddWithValue("embedding_provider", provider);
        command.Parameters.AddWithValue("embedding_model", model);
        command.Parameters.AddWithValue("embedding_dimensions", embeddingDimensions);
        command.Parameters.Add("embedding_values", NpgsqlDbType.Array | NpgsqlDbType.Real).Value = values;
        command.Parameters.AddWithValue("embedding_vector", "[" + string.Join(",", values.Select(static value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]");
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ClearMemoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM incidentcompass.memory_items;", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record TestScope(WebApplicationFactory<Program> Factory, HttpClient Client, string ConnectionString) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}