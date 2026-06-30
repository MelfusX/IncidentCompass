using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi.Dtos;

internal sealed record OpenAiErrorResponse(
    [property: JsonPropertyName("error")] OpenAiError? Error);
