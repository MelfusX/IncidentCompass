using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;

internal sealed record OpenAiToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("function")] OpenAiToolCallFunction? Function);
