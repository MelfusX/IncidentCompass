namespace IncidentCompass.Application.Memory;

internal sealed record MemorySearchRequest(
    string TenantId,
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    IReadOnlyList<float> QueryVector,
    int TopK,
    double MinScore);
