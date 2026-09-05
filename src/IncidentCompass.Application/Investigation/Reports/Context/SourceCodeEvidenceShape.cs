using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Reports.Context;

public static class SourceCodeEvidenceShape
{
    private static readonly HashSet<string> RequiredProperties =
        new(
            [
                "evidenceKind",
                "relativePath",
                "lineStart",
                "lineEnd",
                "excerpt",
                "release",
                "mappingMethod"
            ],
            StringComparer.Ordinal);

    private const int MaxRelativePathCharacters = 1024;
    private const int MaxExcerptCharacters = 1024 * 1024;
    private const int MaxExcerptLines = 100;

    public static bool IsCitable(
        string? domainRef,
        string payloadJson,
        string? currentRelease)
    {
        if (string.IsNullOrWhiteSpace(domainRef) ||
            !domainRef.StartsWith("source:", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(currentRelease))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var payload = document.RootElement;
            var relativePath = ReadString(payload, "relativePath");
            var release = ReadString(payload, "release");
            return HasClosedShape(payload) &&
                ReadString(payload, "evidenceKind") == "SourceCode" &&
                IsSafeRelativePath(relativePath) &&
                TryReadPositiveInt(payload, "lineStart", out var lineStart) &&
                TryReadPositiveInt(payload, "lineEnd", out var lineEnd) &&
                lineStart <= lineEnd &&
                release == currentRelease &&
                domainRef == $"source:{release}:{relativePath}" &&
                ReadString(payload, "mappingMethod") == "heuristic" &&
                IsBoundedExcerpt(ReadString(payload, "excerpt"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxRelativePathCharacters ||
            Path.IsPathRooted(value) || value.IndexOf('\0') >= 0 || value.Contains('\\'))
        {
            return false;
        }

        return !value.Replace('\\', '/').Split('/').Any(segment => segment is "" or "." or "..");
    }

    private static bool HasClosedShape(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.EnumerateObject().Count() == RequiredProperties.Count &&
        payload.EnumerateObject().All(property => RequiredProperties.Contains(property.Name));

    private static bool IsBoundedExcerpt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxExcerptCharacters)
        {
            return false;
        }

        var lines = 1;
        foreach (var character in value)
        {
            if (character == '\n' && ++lines > MaxExcerptLines)
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadPositiveInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value > 0;
    }
}
