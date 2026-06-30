using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;

internal sealed record OpenAiError(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("code")] string? Code);
