using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Intake.Redaction;

public sealed class UserIdentifierPseudonymizer(IOptions<PseudonymizationOptions> options)
{
    internal const string Prefix = "[PSEUDONYM:";
    private const string MissingSaltMarker = "[REDACTED]";

    public NormalizedSignal Protect(NormalizedSignal signal, RedactionSettings settings)
    {
        if (settings.UserIdentifierAttributes.Count == 0)
        {
            return signal;
        }

        var keys = settings.UserIdentifierAttributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return signal with
        {
            Attributes = ProtectNode(signal.Attributes, keys, string.Empty),
            Body = ProtectNode(signal.Body, keys, string.Empty)
        };
    }

    private JsonNode ProtectNode(JsonNode node, HashSet<string> keys, string path)
    {
        return node switch
        {
            JsonObject jsonObject => ProtectObject(jsonObject, keys, path),
            JsonArray jsonArray => ProtectArray(jsonArray, keys, path),
            _ => node.DeepClone()
        };
    }

    private JsonObject ProtectObject(JsonObject source, HashSet<string> keys, string path)
    {
        var result = new JsonObject();
        foreach (var property in source)
        {
            var propertyPath = string.IsNullOrEmpty(path) ? property.Key : path + "." + property.Key;
            if (property.Value is not null && IsIdentifierKey(keys, property.Key, propertyPath))
            {
                result[property.Key] = ProtectIdentifier(property.Value);
                continue;
            }

            result[property.Key] = property.Value is null
                ? null
                : ProtectNode(property.Value, keys, propertyPath);
        }

        return result;
    }

    private JsonArray ProtectArray(JsonArray source, HashSet<string> keys, string path)
    {
        var result = new JsonArray();
        foreach (var item in source)
        {
            result.Add(item is null ? null : ProtectNode(item, keys, path));
        }

        return result;
    }

    private JsonNode ProtectIdentifier(JsonNode value)
    {
        var salt = options.Value.Salt;
        if (string.IsNullOrWhiteSpace(salt))
        {
            return JsonValue.Create(MissingSaltMarker)!;
        }

        var canonicalValue = value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String
            ? jsonValue.GetValue<string>()
            : value.ToJsonString();
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(canonicalValue));
        return JsonValue.Create(Prefix + Convert.ToHexString(digest).ToLowerInvariant() + "]")!;
    }

    private static bool IsIdentifierKey(HashSet<string> keys, string key, string path) =>
        keys.Contains(key) || keys.Contains(path);
}
