using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Infrastructure.Notifications.Telegram;

public static class TelegramNotificationResponseParser
{
    private const int MaximumBodyBytes = 16 * 1024;

    public static async Task<ExternalActionExecutionResult> ParseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Failure(MapStatus(response.StatusCode));
        }

        try
        {
            var body = await ReadBoundedAsync(response.Content, cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("message_id", out var messageId) ||
                !messageId.TryGetInt64(out var parsedMessageId) || parsedMessageId <= 0)
            {
                return Failure("dispatch_outcome_unknown");
            }

            var canonical = new JsonObject
            {
                ["messageId"] = parsedMessageId.ToString(CultureInfo.InvariantCulture),
                ["provider"] = "telegram"
            };
            return new ExternalActionExecutionResult(
                true,
                Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(canonical)),
                "Telegram accepted the notification.",
                AuditProjection: ExternalActionAuditProjection.TelegramMessage(
                    parsedMessageId.ToString(CultureInfo.InvariantCulture)));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Failure("dispatch_outcome_unknown");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumBodyBytes)
        {
            throw new InvalidOperationException("Telegram response exceeds the safe bound.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumBodyBytes)
            {
                throw new InvalidOperationException("Telegram response exceeds the safe bound.");
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static ExternalActionExecutionResult Failure(string code)
    {
        var payload = new JsonObject
        {
            ["code"] = code,
            ["provider"] = "telegram"
        };
        return new ExternalActionExecutionResult(
            false,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            "Telegram did not confirm delivery.",
            code);
    }

    private static string MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "telegram_authentication_failed",
        HttpStatusCode.Forbidden => "telegram_forbidden",
        HttpStatusCode.TooManyRequests => "telegram_rate_limited",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "telegram_request_invalid",
        HttpStatusCode.NotFound => "telegram_not_found",
        HttpStatusCode.RequestTimeout => "dispatch_outcome_unknown",
        _ when (int)statusCode >= 500 => "dispatch_outcome_unknown",
        _ => "telegram_http_error"
    };
}
