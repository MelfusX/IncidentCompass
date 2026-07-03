using IncidentCompass.Application.Core.ModelClients;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        foreach (var result in toolResults.Reverse())
        {
            using var document = JsonDocument.Parse(result);
            if (document.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                return ReadOptionalString(items[0], "artifactId");
            }
        }

        return null;
    }

    public static string? FindMemoryQuote(IReadOnlyList<string> toolResults)
    {
        foreach (var result in toolResults.Reverse())
        {
            using var document = JsonDocument.Parse(result);
            if (document.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                return ReadOptionalString(items[0], "quote");
            }
        }

        return null;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}
