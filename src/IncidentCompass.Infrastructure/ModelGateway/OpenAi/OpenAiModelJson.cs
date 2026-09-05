using System.Text.Json;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal static class OpenAiModelJson
{
    public static JsonSerializerOptions Options => OpenAiCompatibleJson.Options;
}
