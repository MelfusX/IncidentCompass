using System.Text.Json;
using System.Text.RegularExpressions;

namespace IncidentCompass.Infrastructure.Tickets;

internal static partial class GitHubIssueMarker
{
    private const string Prefix = "<!-- incidentcompass-ticket:";
    private const string Suffix = " -->";

    public static string Comment(string marker) => Prefix + marker + Suffix;

    public static bool IsValid(string? marker) =>
        marker is not null && MarkerPattern().IsMatch(marker);

    public static string? ReadFromCanonicalPayload(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("marker", out var marker) &&
                marker.ValueKind == JsonValueKind.String && IsValid(marker.GetString())
                    ? marker.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
