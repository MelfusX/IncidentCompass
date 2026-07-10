using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Memory;

internal sealed class PostgresMemoryRepository(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider) : IMemoryRepository
{
    public Task<IReadOnlyList<MemorySearchMatch>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "search memory",
            () => SearchCoreAsync(request, cancellationToken));

    private async Task<IReadOnlyList<MemorySearchMatch>> SearchCoreAsync(MemorySearchRequest request, CancellationToken cancellationToken)
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
        command.AddParameter("tenant_id", request.TenantId);
        command.AddParameter("embedding_provider", request.EmbeddingProvider);
        command.AddParameter("embedding_model", request.EmbeddingModel);
        command.AddParameter("embedding_dimensions", request.EmbeddingDimensions);
        AddVectorParameter(command, "query_vector", request.QueryVector);
        command.AddParameter("min_score", request.MinScore);
        command.AddParameter("top_k", request.TopK);
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

    public Task<bool> SeedItemExistsAsync(
        MemorySeedItem item,
        CancellationToken cancellationToken)
    {
        return PostgresOperation.ExecuteAsync(
            "check memory seed item",
            () => PostgresMemorySeedWriter.SeedItemExistsAsync(
                dataSourceProvider,
                item,
                cancellationToken));
    }

    public Task UpsertSeedAsync(
        MemorySeedItem item,
        IReadOnlyList<MemorySeedChunk> chunks,
        CancellationToken cancellationToken)
    {
        return PostgresOperation.ExecuteAsync(
            "upsert memory seed",
            () => PostgresMemorySeedWriter.UpsertSeedAsync(
                dataSourceProvider,
                timeProvider.GetUtcNow(),
                item,
                chunks,
                cancellationToken));
    }


    private static void AddVectorParameter(NpgsqlCommand command, string name, IReadOnlyList<float> value)
    {
        command.Parameters.AddWithValue(name, PostgresVectorParameter.From(value));
    }
}
