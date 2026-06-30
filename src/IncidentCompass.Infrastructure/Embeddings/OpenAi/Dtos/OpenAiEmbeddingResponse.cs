using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi.Dtos;

internal sealed record OpenAiEmbeddingResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("data")] IReadOnlyList<OpenAiEmbeddingData>? Data,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);
