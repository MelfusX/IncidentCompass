using IncidentCompass.Application.Observability.CostRollup;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Observability;

internal sealed class PostgresModelCostRollupRepository(PostgresDataSourceProvider dataSourceProvider)
    : IModelCostRollupRepository
{
    internal const string ModelCallsSql = """
        SELECT ledger.created_at_utc, ledger.rationale
        FROM incidentcompass.faults AS fault
        JOIN incidentcompass.triage_ledger AS ledger
          ON ledger.fault_id = fault.id
         AND ledger.event_type = 'ModelCall'
         AND ledger.created_at_utc >= @from_utc
         AND ledger.created_at_utc < @to_utc
        WHERE fault.tenant_id = @tenant_id
        ORDER BY ledger.created_at_utc, ledger.id;
        """;

    private const string PricingSql = """
        SELECT id, provider, model, currency,
               input_token_price_per_million, output_token_price_per_million,
               effective_from_utc, effective_to_utc
        FROM incidentcompass.ai_model_pricing
        WHERE effective_from_utc < @to_utc
          AND (effective_to_utc IS NULL OR effective_to_utc > @from_utc)
        ORDER BY provider COLLATE "C", model COLLATE "C", effective_from_utc, id;
        """;

    public Task<IReadOnlyList<CostRollupHour>> ReadAsync(
        string tenantId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "read tenant model cost rollup",
            () => ReadCoreAsync(tenantId, fromUtc, toUtc, cancellationToken));

    private async Task<IReadOnlyList<CostRollupHour>> ReadCoreAsync(
        string tenantId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        var prices = await ReadPricesAsync(connection, fromUtc, toUtc, cancellationToken);
        var accumulator = new ModelCostRollupAccumulator(prices);
        await using var command = new NpgsqlCommand(ModelCallsSql, connection);
        AddWindowParameters(command, fromUtc, toUtc);
        command.AddParameter("tenant_id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accumulator.Add(
                reader.GetDateTimeOffset(0),
                reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        return accumulator.Build();
    }

    private static async Task<IReadOnlyList<ModelPricingInterval>> ReadPricesAsync(
        NpgsqlConnection connection,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PricingSql, connection);
        AddWindowParameters(command, fromUtc, toUtc);
        var prices = new List<ModelPricingInterval>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            prices.Add(new ModelPricingInterval(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDateTimeOffset(6),
                reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7)));
        }

        return prices;
    }

    private static void AddWindowParameters(
        NpgsqlCommand command,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        command.AddParameter("from_utc", fromUtc);
        command.AddParameter("to_utc", toUtc);
    }
}
