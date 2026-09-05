using System.Text;
using System.Text.Json;

namespace IncidentCompass.Infrastructure.Observability;

internal static class ModelCallUsageParser
{
    private const int MaximumMetadataBytes = 8192;
    private const int MaximumIdentityBytes = 256;

    public static bool TryParse(string? rationale, out ModelCallUsage? usage)
    {
        usage = null;
        if (string.IsNullOrEmpty(rationale) ||
            rationale.Length > MaximumMetadataBytes ||
            Encoding.UTF8.GetByteCount(rationale) > MaximumMetadataBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rationale, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
                !TryReadIdentity(root, "provider", out var provider) ||
                !TryReadIdentity(root, "model", out var model) ||
                !TryReadTokenCount(root, "inputTokens", out var inputTokens) ||
                !TryReadTokenCount(root, "outputTokens", out var outputTokens) ||
                !TryReadTokenCount(root, "totalTokens", out var totalTokens))
            {
                return false;
            }

            usage = new ModelCallUsage(provider!, model!, inputTokens, outputTokens, totalTokens);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return root.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool TryReadIdentity(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null &&
               value.Length > 0 &&
               string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
               Encoding.UTF8.GetByteCount(value) <= MaximumIdentityBytes;
    }

    private static bool TryReadTokenCount(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value) &&
               value >= 0;
    }
}
