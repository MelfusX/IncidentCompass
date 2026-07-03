using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal static class MockIncidentCompassMemoryQuery
{
    public static string CreateSearchArguments(AiModelRequest request)
    {
        var prompt = request.Messages.LastOrDefault(static message => message.Role == AiMessageRole.User)?.Content ?? string.Empty;
        var query = string.Join(' ', new[]
        {
            ReadPromptValue(prompt, "- service:"),
            ReadPromptValue(prompt, "- summary:"),
            ReadPromptValue(prompt, "- errorType:"),
            ReadPromptValue(prompt, "- errorMessage:")
        }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return JsonSerializer.Serialize(new
        {
            query = string.IsNullOrWhiteSpace(query) ? "incident memory" : query
        });
    }

    private static string? ReadPromptValue(string prompt, string prefix)
    {
        foreach (var line in prompt.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }
}
