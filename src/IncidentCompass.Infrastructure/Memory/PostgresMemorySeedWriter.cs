using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Memory;

internal static class PostgresMemorySeedWriter
{
    public static async Task<bool> SeedItemExistsAsync(
        PostgresDataSourceProvider dataSourceProvider,
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.memory_items
                WHERE tenant_id = @tenant_id
                  AND source = @source
                  AND content_hash = @content_hash
                  AND version = @version);
            """, connection);
        AddItemIdentityParameters(command, item);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static async Task UpsertSeedAsync(
        PostgresDataSourceProvider dataSourceProvider,
        DateTimeOffset createdAtUtc,
        MemorySeedItem item,
        IReadOnlyList<MemorySeedChunk> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var itemId = await InsertItemAsync(connection, transaction, item, createdAtUtc, cancellationToken);
            foreach (var chunk in chunks)
            {
                await InsertChunkAsync(connection, transaction, item, itemId, chunk, createdAtUtc, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<Guid> InsertItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedItem item,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash,
                version, tags, created_at_utc)
            VALUES (
                @id, @tenant_id, @kind, @source, @title, @content, @content_hash,
                @version, @tags, @created_at_utc)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, connection, transaction);
        AddItemParameters(command, item, createdAtUtc);
        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        if (insertedId is Guid id)
        {
            return id;
        }

        return await FindItemIdAsync(connection, transaction, item, cancellationToken);
    }

    private static async Task<Guid> FindItemIdAsync(
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
              AND content_hash = @content_hash
              AND version = @version;
            """, connection, transaction);
        AddItemIdentityParameters(command, item);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task InsertChunkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MemorySeedItem item,
        Guid itemId,
        MemorySeedChunk chunk,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.memory_chunks (
                id, memory_item_id, tenant_id, chunk_position, text, text_hash,
                embedding_provider, embedding_model, embedding_dimensions,
                embedding_values, embedding_vector, created_at_utc)
            VALUES (
                @id, @memory_item_id, @tenant_id, @chunk_position, @text, @text_hash,
                @embedding_provider, @embedding_model, @embedding_dimensions,
                @embedding_values, @embedding_vector, @created_at_utc)
            ON CONFLICT DO NOTHING;
            """, connection, transaction);
        command.AddParameter("id", chunk.Id);
        command.AddParameter("memory_item_id", itemId);
        command.AddParameter("tenant_id", item.TenantId);
        command.AddParameter("chunk_position", chunk.Position);
        command.AddParameter("text", chunk.Text);
        command.AddParameter("text_hash", chunk.TextHash);
        command.AddParameter("embedding_provider", chunk.EmbeddingProvider);
        command.AddParameter("embedding_model", chunk.EmbeddingModel);
        command.AddParameter("embedding_dimensions", chunk.EmbeddingDimensions);
        AddRealArrayParameter(command, "embedding_values", chunk.EmbeddingValues);
        AddVectorParameter(command, "embedding_vector", chunk.EmbeddingValues);
        command.AddParameter("created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddItemParameters(
        NpgsqlCommand command,
        MemorySeedItem item,
        DateTimeOffset createdAtUtc)
    {
        command.AddParameter("id", item.Id);
        command.AddParameter("kind", item.Kind);
        command.AddParameter("title", item.Title);
        command.AddParameter("content", item.Content);
        AddTextArrayParameter(command, "tags", item.Tags);
        command.AddParameter("created_at_utc", createdAtUtc);
        AddItemIdentityParameters(command, item);
    }

    private static void AddItemIdentityParameters(NpgsqlCommand command, MemorySeedItem item)
    {
        command.AddParameter("tenant_id", item.TenantId);
        command.AddParameter("source", item.Source);
        command.AddParameter("content_hash", item.ContentHash);
        command.AddParameter("version", item.Version);
    }


    private static void AddRealArrayParameter(NpgsqlCommand command, string name, IReadOnlyList<float> value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Array | NpgsqlDbType.Real).Value = value.ToArray();
    }

    private static void AddTextArrayParameter(NpgsqlCommand command, string name, IReadOnlyList<string> value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Array | NpgsqlDbType.Text).Value = value.ToArray();
    }

    private static void AddVectorParameter(NpgsqlCommand command, string name, IReadOnlyList<float> value)
    {
        command.Parameters.AddWithValue(name, PostgresVectorParameter.From(value));
    }
}
