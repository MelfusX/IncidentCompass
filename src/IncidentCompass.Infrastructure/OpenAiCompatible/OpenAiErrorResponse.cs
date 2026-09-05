using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.OpenAiCompatible;

internal sealed record OpenAiErrorResponse(
    [property: JsonPropertyName("error")] OpenAiError? Error);
