using System.Globalization;
using System.Text.Json;

namespace IncidentCompass.Application.Tickets;

public static class ExistingTicketEvidenceShape
{
    private static readonly HashSet<string> RequiredProperties = new(
        ["evidenceKind", "provider", "repository", "issueNumber", "title", "status", "assignee", "createdAtUtc", "url", "score"],
        StringComparer.Ordinal);

    public static bool IsCitable(string? domainRef, string payloadJson, string? configuredRepository)
    {
        if (string.IsNullOrWhiteSpace(domainRef) || string.IsNullOrWhiteSpace(configuredRepository))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var repository = ReadString(payload, "repository");
            var number = ReadPositiveInt(payload, "issueNumber");
            var expectedUrl = $"https://github.com/{repository}/issues/{number}";
            return HasClosedShape(payload) &&
                ReadString(payload, "evidenceKind") == "ExistingTicket" &&
                ReadString(payload, "provider") == "github" &&
                string.Equals(repository, configuredRepository, StringComparison.Ordinal) &&
                number > 0 &&
                domainRef == $"ticket:github:{repository}:{number}" &&
                string.Equals(ReadString(payload, "url"), expectedUrl, StringComparison.Ordinal) &&
                IsBoundedNonBlank(ReadString(payload, "title"), 512) &&
                IsBoundedNonBlank(ReadString(payload, "status"), 64) &&
                IsValidAssignee(payload) &&
                DateTimeOffset.TryParse(ReadString(payload, "createdAtUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _) &&
                ReadScore(payload) is >= 0 and <= 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasClosedShape(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.EnumerateObject().Count() == RequiredProperties.Count &&
        payload.EnumerateObject().All(property => RequiredProperties.Contains(property.Name));

    private static bool IsValidAssignee(JsonElement payload) =>
        payload.TryGetProperty("assignee", out var value) &&
        (value.ValueKind == JsonValueKind.Null ||
         (value.ValueKind == JsonValueKind.String && IsBoundedNonBlank(value.GetString(), 128)));

    private static bool IsBoundedNonBlank(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadPositiveInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : 0;

    private static double? ReadScore(JsonElement root) =>
        root.TryGetProperty("score", out var value) && value.TryGetDouble(out var score)
            ? score
            : null;
}
