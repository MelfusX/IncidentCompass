using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Memory;

internal static class PostgresMemorySeedItemWriter
{
    public static async Task<Guid?> FindSeedItemIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string owner,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id
            FROM incidentcompass.memory_items
            WHERE tenant_id = @tenant_id
              AND seed_owner = @seed_owner
              AND source = @source
              AND seed_managed = true
            ORDER BY is_active DESC, updated_at_utc DESC, id
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction);
        command.AddParameter("tenant_id", item.TenantId);
        command.AddParameter("seed_owner", owner);
        command.AddParameter("source", item.Source);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    public static async Task<Guid> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset timestamp,
        MemorySeedCorpus corpus,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash, version, tags,
                service_name, component, release_name, is_active, seed_managed, seed_owner,
                seed_generation, created_at_utc, updated_at_utc, superseded_at_utc)
            VALUES (
                @id, @tenant_id, @kind, @source, @title, @content, @content_hash, @version, @tags,
                @service_name, @component, @release_name, true, true, @seed_owner,
                @seed_generation, @timestamp, @timestamp, NULL)
            RETURNING id;
            """, connection, transaction);
        PostgresMemorySeedParameters.AddItem(command, item, timestamp);
        AddGenerationParameters(command, corpus);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset timestamp,
        MemorySeedCorpus corpus,
        Guid itemId,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.memory_items
            SET kind = @kind, title = @title, content = @content,
                version = CASE WHEN content_hash = @content_hash THEN version ELSE version + 1 END,
                content_hash = @content_hash, tags = @tags, service_name = @service_name,
                component = @component, release_name = @release_name, is_active = true,
                seed_managed = true, seed_owner = @seed_owner, seed_generation = @seed_generation,
                updated_at_utc = @timestamp, superseded_at_utc = NULL
            WHERE id = @item_id;
            """, connection, transaction);
        PostgresMemorySeedParameters.AddItem(command, item, timestamp);
        AddGenerationParameters(command, corpus);
        command.AddParameter("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task MarkCurrentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedCorpus corpus,
        string source,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.memory_items
            SET seed_generation = @seed_generation, updated_at_utc = @timestamp
            WHERE tenant_id = @tenant_id AND seed_owner = @seed_owner AND source = @source
              AND seed_managed = true AND is_active = true;
            """, connection, transaction);
        command.AddParameter("tenant_id", corpus.TenantId);
        command.AddParameter("seed_owner", corpus.Owner);
        command.AddParameter("seed_generation", corpus.Generation);
        command.AddParameter("source", source);
        command.AddParameter("timestamp", timestamp);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddGenerationParameters(NpgsqlCommand command, MemorySeedCorpus corpus)
    {
        command.AddParameter("seed_owner", corpus.Owner);
        command.AddParameter("seed_generation", corpus.Generation);
    }
}
