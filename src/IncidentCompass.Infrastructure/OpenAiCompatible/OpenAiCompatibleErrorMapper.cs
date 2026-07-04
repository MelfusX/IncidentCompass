using System.Net;
using System.Text.Json;

namespace IncidentCompass.Infrastructure.OpenAiCompatible;

internal static class OpenAiCompatibleErrorMapper
{
    public static string NormalizeProviderErrorCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "invalid_request",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authentication_error",
            HttpStatusCode.RequestTimeout => "provider_timeout",
            HttpStatusCode.TooManyRequests => "rate_limited",
            _ when (int)statusCode >= 500 => "provider_unavailable",
            _ => "provider_error"
        };
    }

    public static OpenAiErrorResponse? TryReadError(string responseContent)
    {
        try
        {
            return JsonSerializer.Deserialize<OpenAiErrorResponse>(
                responseContent,
                OpenAiCompatibleJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}