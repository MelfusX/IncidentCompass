using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal static class MockIncidentCompassReportScript
{
    public static string PublishArguments(
        string status,
        string summary,
        string classification,
        string confidence,
        IReadOnlyCollection<JsonObject> evidence,
        IReadOnlyCollection<string> limitations,
        string recommendedNextAction)
    {
        return new JsonObject
        {
            ["report_json"] = new JsonObject
            {
                ["status"] = status,
                ["summary"] = summary,
                ["classification"] = classification,
                ["confidence"] = confidence,
                ["documentationFit"] = "Missing",
                ["evidence"] = new JsonArray(evidence.Select(static item => item.DeepClone()).ToArray()),
                ["limitations"] = new JsonArray(limitations.Select(static item => JsonValue.Create(item) as JsonNode).ToArray()),
                ["recommendedNextAction"] = recommendedNextAction
            }
        }.ToJsonString();
    }

    public static JsonObject Evidence(string referenceId, string? quote = null)
    {
        var evidence = new JsonObject { ["referenceId"] = referenceId };
        if (!string.IsNullOrWhiteSpace(quote))
        {
            evidence["quote"] = quote;
        }

        return evidence;
    }

    public static string FindPromptArtifactId(AiModelRequest request, string kind)
    {
        var prompt = request.Messages.FirstOrDefault(static message => message.Role == AiMessageRole.User)?.Content ?? string.Empty;
        foreach (var line in prompt.Split('\n'))
        {
            if (!line.Contains("kind=" + kind, StringComparison.Ordinal))
            {
                continue;
            }

            var marker = "artifact:";
            var start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var rest = line[(start + marker.Length)..].Trim();
                return rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }
        }

        return Guid.Empty.ToString();
    }

    public static string? FindMemoryArtifactId(IReadOnlyList<string> toolResults)
    {
        return FindMemoryItemValue(toolResults, "artifactId");
    }

    public static string? FindMemoryQuote(IReadOnlyList<string> toolResults)
    {
        return FindMemoryItemValue(toolResults, "quote");
    }

    private static string? FindMemoryItemValue(IReadOnlyList<string> toolResults, string propertyName)
    {
        foreach (var result in toolResults.Reverse())
        {
            using var document = JsonDocument.Parse(result);
            if (document.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                return ReadOptionalString(FindPreferredMemoryItem(items), propertyName);
            }
        }

        return null;
    }

    private static JsonElement FindPreferredMemoryItem(JsonElement items)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (ReadOptionalString(item, "title")?.Contains("Runbook", StringComparison.OrdinalIgnoreCase) == true)
            {
                return item;
            }
        }

        return items[0];
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
