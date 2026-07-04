using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IncidentCompass.Application.Core.Serialization;

internal static class CanonicalJsonSerializer
{
    public static string Canonicalize(JsonNode? node)
    {
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();
    }

    public static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    // U+001E (ASCII Record Separator) never appears in canonical JSON output produced by
    // Canonicalize (control characters inside JSON string literals are always \u-escaped),
    // so concatenating the two canonical strings around it is unambiguous: no pair of
    // (canonicalJson1, canonicalJson2) values can collide with a different pair by shifting
    // where one string ends and the other begins.
    private const char UnambiguousSeparator = '\u001e';

    public static string ComputeSha256Hex(string canonicalJson1, string canonicalJson2)
    {
        var combined = canonicalJson1 + UnambiguousSeparator + canonicalJson2;
        return ComputeSha256Hex(combined);
    }

    public static string ComputeSha256Hex(string canonicalJson)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject jsonObject:
                WriteObject(jsonObject, builder);
                break;
            case JsonArray jsonArray:
                WriteArray(jsonArray, builder);
                break;
            case JsonValue jsonValue:
                builder.Append(jsonValue.ToJsonString());
                break;
            default:
                builder.Append(node.ToJsonString());
                break;
        }
    }

    private static void WriteObject(JsonObject jsonObject, StringBuilder builder)
    {
        builder.Append('{');
        var isFirst = true;
        foreach (var key in jsonObject.Select(property => property.Key).Order(StringComparer.Ordinal))
        {
            if (!isFirst)
            {
                builder.Append(',');
            }

            isFirst = false;
            builder.Append(JsonSerializer.Serialize(key));
            builder.Append(':');
            Write(jsonObject[key], builder);
        }

        builder.Append('}');
    }

    private static void WriteArray(JsonArray jsonArray, StringBuilder builder)
    {
        builder.Append('[');
        for (var index = 0; index < jsonArray.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            Write(jsonArray[index], builder);
        }

        builder.Append(']');
    }
}