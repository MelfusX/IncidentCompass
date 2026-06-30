using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;

internal sealed record OpenAiTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiToolFunction Function);
