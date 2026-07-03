using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class MemoryWorkerOutputParser
{
    public static MemoryWorkerOutput Parse(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (!root.TryGetProperty("matched", out var matchedElement) ||
            matchedElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException("Memory worker output is missing boolean matched.");
        }

        var items = ReadItems(root);
        var matched = matchedElement.GetBoolean();
        if (matched && items.Count == 0)
        {
            throw new InvalidOperationException("Memory worker output matched=true requires at least one item.");
        }

        var noMatchReason = root.TryGetProperty("noMatchReason", out var reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()
                : null;
        return new MemoryWorkerOutput(matched, items, noMatchReason);
    }

    private static IReadOnlyList<MemoryWorkerOutputItem> ReadItems(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var itemsElement) ||
            itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Memory worker output is missing items array.");
        }

        var items = new List<MemoryWorkerOutputItem>();
        foreach (var itemElement in itemsElement.EnumerateArray())
        {
            items.Add(new MemoryWorkerOutputItem(
                ReadRequiredString(itemElement, "artifactId"),
                ReadRequiredString(itemElement, "title"),
                ReadOptionalString(itemElement, "quote") ?? string.Empty,
                ReadOptionalDouble(itemElement, "score")));
        }

        return items;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        var value = ReadOptionalString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Memory worker output is missing string {propertyName}.")
            : value;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static double? ReadOptionalDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out var value)
            ? value
            : null;
    }
}
