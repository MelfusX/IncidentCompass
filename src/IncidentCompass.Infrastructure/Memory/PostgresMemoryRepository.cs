using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;
namespace IncidentCompass.Infrastructure.Memory;

internal sealed class PostgresMemoryRepository(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider) : IMemoryRepository
{
    public async Task<IReadOnlyList<MemorySearchMatch>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH candidate_chunks AS MATERIALIZED (
                SELECT mc.id AS chunk_id,
                       mc.memory_item_id,
                       mi.kind,
                       mi.source,
                       mi.title,
                       mc.chunk_position,
                       mc.text,
                       mc.embedding_vector
                FROM incidentcompass.memory_chunks mc
                JOIN incidentcompass.memory_items mi ON mi.id = mc.memory_item_id
                WHERE mc.tenant_id = @tenant_id
                  AND mc.embedding_provider = @embedding_provider
                  AND mc.embedding_model = @embedding_model
                  AND mc.embedding_dimensions = @embedding_dimensions
            ),
            scored_chunks AS (
                SELECT *,
                       1 - (embedding_vector <=> @query_vector) AS score
                FROM candidate_chunks
            )
            SELECT memory_item_id, chunk_id, kind, source, title, chunk_position, text, score
            FROM scored_chunks
            WHERE score >= @min_score
            ORDER BY score DESC, chunk_id
            LIMIT @top_k;
            """, connection);
        AddParameter(command, "tenant_id", request.TenantId);
        AddParameter(command, "embedding_provider", request.EmbeddingProvider);
        AddParameter(command, "embedding_model", request.EmbeddingModel);
        AddParameter(command, "embedding_dimensions", request.EmbeddingDimensions);
        AddVectorParameter(command, "query_vector", request.QueryVector);
        AddParameter(command, "min_score", request.MinScore);
        AddParameter(command, "top_k", request.TopK);
        var results = new List<MemorySearchMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MemorySearchMatch(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetDouble(7)));
        }
        return results;
    }
    public async Task UpsertSeedAsync(
        MemorySeedItem item,
        IReadOnlyList<MemorySeedChunk> chunks,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var itemId = await UpsertItemAsync(connection, transaction, item, createdAtUtc, cancellationToken);
            await DeleteChunksAsync(connection, transaction, itemId, cancellationToken);
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
    private static async Task<Guid> UpsertItemAsync(
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
            ON CONFLICT (tenant_id, source, content_hash, version)
            DO UPDATE SET
                title = EXCLUDED.title,
                content = EXCLUDED.content,
                tags = EXCLUDED.tags
            RETURNING id;
            """, connection, transaction);
        AddParameter(command, "id", item.Id);
        AddParameter(command, "tenant_id", item.TenantId);
        AddParameter(command, "kind", item.Kind);
        AddParameter(command, "source", item.Source);
        AddParameter(command, "title", item.Title);
        AddParameter(command, "content", item.Content);
        AddParameter(command, "content_hash", item.ContentHash);
        AddParameter(command, "version", item.Version);
        AddTextArrayParameter(command, "tags", item.Tags);
        AddParameter(command, "created_at_utc", createdAtUtc);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
    private static async Task DeleteChunksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM incidentcompass.memory_chunks WHERE memory_item_id = @memory_item_id;",
            connection,
            transaction);
        AddParameter(command, "memory_item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                @embedding_values, @embedding_vector, @created_at_utc);
            """, connection, transaction);
        AddParameter(command, "id", chunk.Id);
        AddParameter(command, "memory_item_id", itemId);
        AddParameter(command, "tenant_id", item.TenantId);
        AddParameter(command, "chunk_position", chunk.Position);
        AddParameter(command, "text", chunk.Text);
        AddParameter(command, "text_hash", chunk.TextHash);
        AddParameter(command, "embedding_provider", chunk.EmbeddingProvider);
        AddParameter(command, "embedding_model", chunk.EmbeddingModel);
        AddParameter(command, "embedding_dimensions", chunk.EmbeddingDimensions);
        AddRealArrayParameter(command, "embedding_values", chunk.EmbeddingValues);
        AddVectorParameter(command, "embedding_vector", chunk.EmbeddingValues);
        AddParameter(command, "created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddParameter(NpgsqlCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
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
