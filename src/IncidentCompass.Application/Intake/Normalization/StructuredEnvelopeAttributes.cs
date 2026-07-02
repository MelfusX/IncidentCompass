using System.Text.Json.Nodes;

namespace IncidentCompass.Application.Intake.Normalization;

// Convention (deliberate MVP interpretation -- the plan does not enumerate attribute keys):
// structured sources (tester, otel) may populate these keys inside `attributes`: errorType
// (string), errorMessage (string), operationName (string), httpMethod (string), httpRoute
// (string), httpStatusCode (number), durationMs (number).
internal static class StructuredEnvelopeAttributes
{
    public static string? GetString(JsonNode? attributes, string propertyName)
    {
        if (attributes is not JsonObject jsonObject)
        {
            return null;
        }

        if (!jsonObject.TryGetPropertyValue(propertyName, out var value) || value is not JsonValue jsonValue)
        {
            return null;
        }

        return jsonValue.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? jsonValue.GetValue<string>()
            : null;
    }

    public static int? GetInt(JsonNode? attributes, string propertyName)
    {
        if (attributes is not JsonObject jsonObject)
        {
            return null;
        }

        if (!jsonObject.TryGetPropertyValue(propertyName, out var value) || value is not JsonValue jsonValue)
        {
            return null;
        }

        if (jsonValue.GetValueKind() != System.Text.Json.JsonValueKind.Number)
        {
            return null;
        }

        return jsonValue.TryGetValue<int>(out var intValue) ? intValue : null;
    }
}
