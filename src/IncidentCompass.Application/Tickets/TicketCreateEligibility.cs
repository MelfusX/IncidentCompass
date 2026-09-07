using System.Text.Json;

namespace IncidentCompass.Application.Tickets;

public static class TicketCreateEligibility
{
    public static bool IsRepositoryBoundNoMatch(
        JsonElement output,
        string expectedProvider,
        string expectedRepository)
    {
        if (output.ValueKind != JsonValueKind.Object ||
            output.EnumerateObject().Count() != 8 ||
            !output.TryGetProperty("matched", out var matched) ||
            matched.ValueKind != JsonValueKind.False ||
            !ReadExact(output, "message", "no matches") ||
            !ReadExact(output, "outcome", "no_match") ||
            !ReadExact(output, "provider", expectedProvider) ||
            !ReadExact(output, "repository", expectedRepository) ||
            !output.TryGetProperty("code", out var code) ||
            code.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(code.GetString()) || code.GetString()!.Length > 128 ||
            !output.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 0)
        {
            return false;
        }

        return ReadExact(output, "noMatchReason", code.GetString()!);
    }

    private static bool ReadExact(JsonElement output, string name, string expected) =>
        output.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}
