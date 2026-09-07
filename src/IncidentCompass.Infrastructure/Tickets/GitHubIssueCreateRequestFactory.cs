using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssueCreateRequestFactory
{
    public static HttpRequestMessage Create(
        ReadOnlyMemory<byte> canonicalPayload,
        GitHubIssuesOptions options)
    {
        var payload = ReadPayload(canonicalPayload);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/repos/{options.Owner}/{options.Repository}/issues")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                CanonicalJsonSerializer.Canonicalize(new JsonObject
                {
                    ["body"] = payload.Body,
                    ["title"] = payload.Title
                })))
        };
        AddGitHubHeaders(request, options.Token!);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return request;
    }

    public static string ReadMarker(ReadOnlyMemory<byte> canonicalPayload) =>
        ReadPayload(canonicalPayload).Marker;

    public static void AddGitHubHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("IncidentCompass/0.3");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static (string Marker, string Title, string Body) ReadPayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is < 1 or > 8192)
        {
            throw new InvalidOperationException("GitHub issue payload is invalid.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 5 ||
            !root.TryGetProperty("schemaVersion", out var version) ||
            !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
            !root.TryGetProperty("originReportId", out var report) ||
            report.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(report.GetString(), "N", out _) ||
            !root.TryGetProperty("marker", out var markerNode) ||
            markerNode.ValueKind != JsonValueKind.String ||
            !GitHubIssueMarker.IsValid(markerNode.GetString()) ||
            !root.TryGetProperty("title", out var titleNode) || titleNode.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("body", out var bodyNode) || bodyNode.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("GitHub issue payload is invalid.");
        }

        var marker = markerNode.GetString()!;
        var title = titleNode.GetString()!;
        var body = bodyNode.GetString()!;
        if (string.IsNullOrWhiteSpace(title) || Encoding.UTF8.GetByteCount(title) > 256 ||
            string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > 4096 ||
            !body.EndsWith(GitHubIssueMarker.Comment(marker), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GitHub issue payload is invalid.");
        }

        var canonical = Encoding.UTF8.GetBytes(
            CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(payload.Span)));
        if (!payload.Span.SequenceEqual(canonical))
        {
            throw new InvalidOperationException("GitHub issue payload is not canonical.");
        }

        return (marker, title, body);
    }
}
