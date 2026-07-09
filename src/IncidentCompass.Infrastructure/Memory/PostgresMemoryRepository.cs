using IncidentCompass.Application.Memory;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

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
                       mc.embedding_vector,
                       mi.service_name,
                       mi.component,
                       mi.release_name
                FROM incidentcompass.memory_chunks mc
                JOIN incidentcompass.memory_items mi ON mi.id = mc.memory_item_id
                WHERE mc.tenant_id = @tenant_id
                  AND mi.is_active = true
                  AND mc.embedding_provider = @embedding_provider
                  AND mc.embedding_model = @embedding_model
                  AND mc.embedding_dimensions = @embedding_dimensions
            ),
            scored_chunks AS (
                SELECT *,
                       1 - (embedding_vector <=> @query_vector) AS score
                FROM candidate_chunks
            )
            SELECT memory_item_id, chunk_id, kind, source, title, chunk_position, text, score,
                   service_name, component, release_name
            FROM scored_chunks
            WHERE score >= @min_score
            ORDER BY score DESC, chunk_id
            LIMIT @top_k;
            """, connection);
        command.AddParameter("tenant_id", request.TenantId);
        command.AddParameter("embedding_provider", request.EmbeddingProvider);
        command.AddParameter("embedding_model", request.EmbeddingModel);
        command.AddParameter("embedding_dimensions", request.EmbeddingDimensions);
        command.Parameters.AddWithValue("query_vector", PostgresVectorParameter.From(request.QueryVector));
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
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
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

    public Task DeactivateMissingSeedsAsync(
        string tenantId,
        IReadOnlyCollection<string> activeSources,
        CancellationToken cancellationToken)
    {
        return PostgresMemorySeedWriter.DeactivateMissingSeedsAsync(
            dataSourceProvider,
            timeProvider.GetUtcNow(),
            tenantId,
            activeSources,
            cancellationToken);
    }
}
