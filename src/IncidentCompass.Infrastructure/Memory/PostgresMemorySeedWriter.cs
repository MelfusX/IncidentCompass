using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Memory;

internal static class PostgresMemorySeedWriter
{
    public static async Task<bool> SeedItemExistsAsync(
        PostgresDataSourceProvider dataSourceProvider,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        return await CurrentSeedMatchesAsync(connection, transaction: null, item, cancellationToken);
    }

    public static async Task UpsertSeedAsync(
        PostgresDataSourceProvider dataSourceProvider,
        DateTimeOffset timestamp,
        MemorySeedItem item,
        IReadOnlyList<MemorySeedChunk> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockSourceAsync(connection, transaction, item, cancellationToken);
            if (await CurrentSeedMatchesAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var itemId = await FindSeedItemIdAsync(connection, transaction, item, cancellationToken);
            if (itemId.HasValue)
            {
                await UpdateItemAsync(connection, transaction, itemId.Value, item, timestamp, cancellationToken);
            }
            else
            {
                itemId = await InsertItemAsync(connection, transaction, item, timestamp, cancellationToken);
            }

            await PostgresMemorySeedChunkWriter.ReplaceAsync(
                connection, transaction, itemId.Value, item, chunks, timestamp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static async Task DeactivateMissingSeedsAsync(
        PostgresDataSourceProvider dataSourceProvider,
        DateTimeOffset timestamp,
        string tenantId,
        IReadOnlyCollection<string> activeSources,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.memory_items
            SET is_active = false,
                superseded_at_utc = @timestamp,
                updated_at_utc = @timestamp
            WHERE tenant_id = @tenant_id
              AND seed_managed = true
              AND is_active = true
              AND NOT (source = ANY(@active_sources));
            """, connection);
        command.AddParameter("tenant_id", tenantId);
        PostgresMemorySeedParameters.AddTextArray(command, "active_sources", activeSources);
        command.AddParameter("timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> CurrentSeedMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.memory_items
                WHERE tenant_id = @tenant_id
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
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task LockSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@identity, 0));",
            connection,
            transaction);
        command.AddParameter("identity", item.TenantId + ":" + item.Source);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> FindSeedItemIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id
            FROM incidentcompass.memory_items
            WHERE tenant_id = @tenant_id
              AND source = @source
              AND seed_managed = true
            ORDER BY is_active DESC, updated_at_utc DESC, id
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction);
        command.AddParameter("tenant_id", item.TenantId);
        command.AddParameter("source", item.Source);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task<Guid> InsertItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedItem item,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash, version, tags,
                service_name, component, release_name, is_active, seed_managed,
                created_at_utc, updated_at_utc, superseded_at_utc)
            VALUES (
                @id, @tenant_id, @kind, @source, @title, @content, @content_hash, @version, @tags,
                @service_name, @component, @release_name, true, true,
                @timestamp, @timestamp, NULL)
            RETURNING id;
            """, connection, transaction);
        PostgresMemorySeedParameters.AddItem(command, item, timestamp);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task UpdateItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid itemId,
        MemorySeedItem item,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.memory_items
            SET kind = @kind, title = @title, content = @content,
                version = CASE WHEN content_hash = @content_hash THEN version ELSE version + 1 END,
                content_hash = @content_hash, tags = @tags,
                service_name = @service_name, component = @component, release_name = @release_name,
                is_active = true, seed_managed = true, updated_at_utc = @timestamp, superseded_at_utc = NULL
            WHERE id = @item_id;
            """, connection, transaction);
        PostgresMemorySeedParameters.AddItem(command, item, timestamp);
        command.AddParameter("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
