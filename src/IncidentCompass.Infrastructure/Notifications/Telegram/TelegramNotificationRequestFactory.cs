using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.Infrastructure.Notifications.Telegram;

internal static class TelegramNotificationRequestFactory
{
    public static HttpRequestMessage Create(
        ReadOnlyMemory<byte> canonicalPayload,
        TelegramOptions options)
    {
        var text = ReadText(canonicalPayload, options.RouteId);
        var body = new JsonObject
        {
            ["chat_id"] = options.ChatId,
            ["disable_notification"] = false,
            ["protect_content"] = true,
            ["text"] = text
        };
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/bot" + options.BotToken + "/sendMessage")
        {
            Content = new ByteArrayContent(
                Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(body)))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return request;
    }

    private static string ReadText(ReadOnlyMemory<byte> payload, string expectedRouteId)
    {
        if (payload.Length is < 1 or > 4096)
        {
            throw new InvalidOperationException("Telegram payload is invalid.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
            !root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schema) || schema != 1 ||
            !root.TryGetProperty("originReportId", out var report) || report.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(report.GetString(), "N", out _) ||
            !root.TryGetProperty("routeId", out var route) || route.ValueKind != JsonValueKind.String ||
            !string.Equals(route.GetString(), expectedRouteId, StringComparison.Ordinal) ||
            !root.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Telegram payload is invalid.");
        }

        var text = textNode.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 2048)
        {
            throw new InvalidOperationException("Telegram message is invalid.");
        }

        var canonical = Encoding.UTF8.GetBytes(
            CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(payload.Span)));
        if (!payload.Span.SequenceEqual(canonical))
        {
            throw new InvalidOperationException("Telegram payload is not canonical.");
        }

        return text;
    }
}
