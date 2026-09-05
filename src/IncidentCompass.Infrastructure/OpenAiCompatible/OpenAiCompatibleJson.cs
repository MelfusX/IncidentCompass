using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentCompass.Infrastructure.OpenAiCompatible;

internal static class OpenAiCompatibleJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
