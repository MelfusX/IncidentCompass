using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi.Dtos;

internal sealed record OpenAiEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);
