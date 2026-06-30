using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;

internal sealed record OpenAiResponseMessage(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls);
