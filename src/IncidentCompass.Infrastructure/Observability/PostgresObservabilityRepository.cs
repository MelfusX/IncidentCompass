using IncidentCompass.Domain.Observability;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Observability;

/// DORMANT: reserved for IC-BL-014 cost rollup work.
internal sealed class PostgresObservabilityRepository(PostgresDataSourceProvider dataSourceProvider)
    : IPricingRepository
{
    public async Task<PricingRecord?> GetEffectivePricingAsync(
        string provider,
        string model,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, provider, model, currency, input_token_price_per_million,
                   output_token_price_per_million, embedding_token_price_per_million,
                   effective_from_utc, effective_to_utc
            FROM incidentcompass.ai_model_pricing
            WHERE provider = @provider
              AND model = @model
              AND effective_from_utc <= @used_at_utc
              AND (effective_to_utc IS NULL OR effective_to_utc > @used_at_utc)
            ORDER BY effective_from_utc DESC
            LIMIT 1;
            """, connection);
        AddParameter(command, "provider", provider);
        AddParameter(command, "model", model);
        AddParameter(command, "used_at_utc", usedAtUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? MapPricingRecord(reader)
            : null;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await dataSourceProvider.OpenConnectionAsync(cancellationToken);
    }

    private static PricingRecord MapPricingRecord(NpgsqlDataReader reader)
    {
        return new PricingRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            GetDateTimeOffset(reader, 7),
            reader.IsDBNull(8) ? null : GetDateTimeOffset(reader, 8));
    }

    private static DateTimeOffset GetDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
