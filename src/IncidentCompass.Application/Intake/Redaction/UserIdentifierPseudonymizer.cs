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
    internal const string Prefix = "[PSEUDONYM:v1:";
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

    internal bool IsCanonicalPseudonym(JsonNode? value, string path)
    {
        var salt = options.Value.Salt;
        if (string.IsNullOrWhiteSpace(salt) ||
            value is not JsonValue jsonValue ||
            jsonValue.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        var token = jsonValue.GetValue<string>();
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || !token.EndsWith(']'))
        {
            return false;
        }

        var parts = token[Prefix.Length..^1].Split(':');
        if (parts.Length != 2 || !IsSha256Hex(parts[0]) || !IsSha256Hex(parts[1]))
        {
            return false;
        }

        var expectedProof = ComputeHexDigest(salt, $"v1:{path}:{parts[0]}");
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedProof),
            Convert.FromHexString(parts[1]));
    }

    private JsonNode ProtectNode(JsonNode node, HashSet<string> keys, string path) =>
        node switch
        {
            JsonObject jsonObject => ProtectObject(jsonObject, keys, path),
            JsonArray jsonArray => ProtectArray(jsonArray, keys, path),
            _ => node.DeepClone()
        };

    private JsonObject ProtectObject(JsonObject source, HashSet<string> keys, string path)
    {
        var result = new JsonObject();
        foreach (var property in source)
        {
            var propertyPath = string.IsNullOrEmpty(path) ? property.Key : path + "." + property.Key;
            result[property.Key] = property.Value is not null && IsIdentifierKey(keys, property.Key, propertyPath)
                ? ProtectIdentifier(property.Value, propertyPath)
                : property.Value is null ? null : ProtectNode(property.Value, keys, propertyPath);
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

    private JsonNode ProtectIdentifier(JsonNode value, string path)
    {
        var salt = options.Value.Salt;
        if (string.IsNullOrWhiteSpace(salt))
        {
            return JsonValue.Create(MissingSaltMarker)!;
        }

        var canonicalValue = value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String
            ? jsonValue.GetValue<string>()
            : value.ToJsonString();
        var digest = ComputeHexDigest(salt, canonicalValue);
        var proof = ComputeHexDigest(salt, $"v1:{path}:{digest}");
        return JsonValue.Create($"{Prefix}{digest}:{proof}]")!;
    }

    private static string ComputeHexDigest(string salt, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(salt),
            Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsIdentifierKey(HashSet<string> keys, string key, string path) =>
        keys.Contains(key) || keys.Contains(path);
}