using IncidentCompass.Infrastructure.Observability;
using IncidentCompass.Domain.Observability;
using IncidentCompass.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostgresObservabilityRepositoryTests(
    PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task RequestLogPersistence_StoresSanitizedMetadataAndUsage()
    {
        using var scope = await CreateScopeAsync();
        await CleanObservabilityTablesAsync(scope.ConnectionString);
        await InsertPricingAsync(
            scope.ConnectionString,
            "fake",
            "test-model",
            inputPrice: 10m,
            outputPrice: 20m,
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"));
        var repository = scope.Services.GetRequiredService<IAiRequestLogRepository>();

        var entry = new AiRequestLogEntry(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "v1",
            "alice",
            "tenant-a",
            "correlation-1",
            "fake",
            "test-model",
            "Succeeded",
            ErrorCode: null,
            TimeSpan.FromMilliseconds(42),
            InputTokens: 1000,
            OutputTokens: 2000,
            TotalTokens: 3000,
            EmbeddingTokens: null,
            EstimatedCost: 0.05m,
            CostCurrency: "USD",
            DateTimeOffset.Parse("2026-05-15T12:00:00Z"));

        await repository.AddAsync(entry, TestContext.Current.CancellationToken);

        var persisted = await ReadPersistedLogAsync(scope.ConnectionString, entry.RequestId);
        Assert.Equal("v1", persisted.ApiVersion);
        Assert.Equal("alice", persisted.UserId);
        Assert.Equal("tenant-a", persisted.TenantId);
        Assert.Equal("fake", persisted.Provider);
        Assert.Equal("test-model", persisted.Model);
        Assert.Equal("Succeeded", persisted.Status);
        Assert.Equal(42, persisted.LatencyMs);
    }

    [DockerAvailableFact]
    public async Task Pricing_UsesHistoricalPricingByEffectiveDate()
    {
        using var scope = await CreateScopeAsync();
        await CleanObservabilityTablesAsync(scope.ConnectionString);
        await InsertPricingAsync(
            scope.ConnectionString,
            "fake",
            "test-model",
            inputPrice: 1m,
            outputPrice: 2m,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"));
        await InsertPricingAsync(
            scope.ConnectionString,
            "fake",
            "test-model",
            inputPrice: 10m,
            outputPrice: 20m,
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"));
        var pricingRepository = scope.Services.GetRequiredService<IPricingRepository>();

        var historicalPricing = await pricingRepository.GetEffectivePricingAsync(
            "fake",
            "test-model",
            DateTimeOffset.Parse("2026-04-15T00:00:00Z"),
            TestContext.Current.CancellationToken);
        var currentPricing = await pricingRepository.GetEffectivePricingAsync(
            "fake",
            "test-model",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(historicalPricing);
        Assert.Equal(1m, historicalPricing.InputTokenPricePerMillion);
        Assert.NotNull(currentPricing);
        Assert.Equal(10m, currentPricing.InputTokenPricePerMillion);
    }

    private async Task<RepositoryScope> CreateScopeAsync()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IncidentCompass"] = connectionString,
                ["IncidentCompass:Postgres:ConnectionStringName"] = "IncidentCompass"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestApplication(configuration);
        services.AddInfrastructure(configuration);
        var serviceProvider = services.BuildServiceProvider();

        return new RepositoryScope(serviceProvider, connectionString);
    }

    private static async Task CleanObservabilityTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            TRUNCATE TABLE
                incidentcompass.ai_request_logs,
                incidentcompass.ai_model_pricing;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPricingAsync(
        string connectionString,
        string provider,
        string model,
        decimal inputPrice,
        decimal outputPrice,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.ai_model_pricing (
                id, provider, model, currency, input_token_price_per_million,
                output_token_price_per_million, embedding_token_price_per_million,
                effective_from_utc, effective_to_utc)
            VALUES (
                @id, @provider, @model, 'USD', @input_price,
                @output_price, NULL, @effective_from_utc, @effective_to_utc);
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("model", model);
        command.Parameters.AddWithValue("input_price", inputPrice);
        command.Parameters.AddWithValue("output_price", outputPrice);
        command.Parameters.AddWithValue("effective_from_utc", effectiveFromUtc);
        command.Parameters.AddWithValue("effective_to_utc", effectiveToUtc ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PersistedLog> ReadPersistedLogAsync(
        string connectionString,
        Guid requestId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT api_version, user_id, tenant_id, provider, model, status, latency_ms
            FROM incidentcompass.ai_request_logs
            WHERE request_id = @request_id;
            """, connection);
        command.Parameters.AddWithValue("request_id", requestId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("AI request log was not found.");
        }

        return new PersistedLog(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6));
    }

    private sealed record RepositoryScope(
        ServiceProvider Services,
        string ConnectionString)
        : IDisposable
    {
        public void Dispose()
        {
            Services.Dispose();
        }
    }

    private sealed record PersistedLog(
        string ApiVersion,
        string UserId,
        string TenantId,
        string Provider,
        string Model,
        string Status,
        int LatencyMs);
}
