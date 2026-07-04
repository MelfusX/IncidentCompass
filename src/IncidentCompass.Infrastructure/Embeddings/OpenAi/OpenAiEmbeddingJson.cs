using System.Text.Json;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi;

internal static class OpenAiEmbeddingJson
{
    public static JsonSerializerOptions Options => OpenAiCompatibleJson.Options;
}