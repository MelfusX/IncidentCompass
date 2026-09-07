using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.OpenAiCompatible;

internal sealed record OpenAiError(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("code")] string? Code);
