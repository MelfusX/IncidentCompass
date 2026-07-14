using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Memory;

internal static class PostgresMemorySeedWriter
{
    public static async Task<bool> SeedItemExistsAsync(
        PostgresDataSourceProvider dataSourceProvider,
        string owner,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        return await CurrentSeedMatchesAsync(connection, null, owner, item, cancellationToken);
    }

    public static Task ReconcileAsync(
        PostgresDataSourceProvider dataSourceProvider,
        DateTimeOffset timestamp,
        MemorySeedCorpus corpus,
        CancellationToken cancellationToken) =>
        PostgresMemorySeedCorpusReconciler.ReconcileAsync(
            dataSourceProvider, timestamp, corpus, cancellationToken);

    internal static async Task<bool> CurrentSeedMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string owner,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.memory_items
                WHERE tenant_id = @tenant_id
                  AND seed_owner = @seed_owner
                  AND source = @source
                  AND seed_managed = true
                  AND is_active = true
                  AND kind = @kind
                  AND title = @title
                  AND content_hash = @content_hash
                  AND tags = @tags
                  AND service_name IS NOT DISTINCT FROM @service_name
                  AND component IS NOT DISTINCT FROM @component
                  AND release_name IS NOT DISTINCT FROM @release_name);
            """, connection, transaction);
        PostgresMemorySeedParameters.AddItem(command, item, DateTimeOffset.UnixEpoch);
        command.AddParameter("seed_owner", owner);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}