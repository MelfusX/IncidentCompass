using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi.Dtos;

internal sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);
