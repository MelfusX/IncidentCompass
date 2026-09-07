using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.Infrastructure.Tickets;

internal static partial class GitHubIssueCommentMarker
{
    private const string Prefix = "<!-- incidentcompass-ticket-comment:";
    private const string Suffix = " -->";

    public static string Create(string proposalKey, Guid originReportId, string ticketId)
    {
        var value = Encoding.UTF8.GetBytes(
            proposalKey + "\n" + originReportId.ToString("N") + "\n" + ticketId);
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    public static string Comment(string marker) => Prefix + marker + Suffix;

    public static bool IsValid(string? marker) =>
        marker is not null && MarkerPattern().IsMatch(marker);

    public static bool TryReadPayload(
        ReadOnlyMemory<byte> payload,
        out (Guid OriginReportId, string TicketId, int IssueNumber, string Marker, string Body) value)
    {
        value = default;
        if (payload.Length is < 1 or > 8192)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 5 ||
                !root.TryGetProperty("schemaVersion", out var version) ||
                !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
                !root.TryGetProperty("originReportId", out var report) ||
                report.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(report.GetString(), "N", out var reportId) ||
                !root.TryGetProperty("ticketId", out var ticket) ||
                ticket.ValueKind != JsonValueKind.String ||
                !int.TryParse(ticket.GetString(), out var issueNumber) || issueNumber <= 0 ||
                ticket.GetString() != issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !root.TryGetProperty("marker", out var markerNode) ||
                markerNode.ValueKind != JsonValueKind.String || !IsValid(markerNode.GetString()) ||
                !root.TryGetProperty("body", out var bodyNode) || bodyNode.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var marker = markerNode.GetString()!;
            var body = bodyNode.GetString()!;
            if (string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > 4096 ||
                !body.EndsWith(Comment(marker), StringComparison.Ordinal))
            {
                return false;
            }

            var canonical = Encoding.UTF8.GetBytes(
                CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(payload.Span)));
            if (!payload.Span.SequenceEqual(canonical))
            {
                return false;
            }

            value = (reportId, issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                issueNumber, marker, body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
