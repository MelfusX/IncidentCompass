using System.Net;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal sealed class OpenAiModelRetryPolicy
{
    private readonly OpenAiCompatibleRetryPolicy retryPolicy = new();

    public bool ShouldRetry(HttpStatusCode statusCode)
    {
        return retryPolicy.ShouldRetry(statusCode);
    }

    public Task DelayBeforeRetryAsync(
        OpenAiCompatibleModelClientOptions clientOptions,
        HttpResponseMessage? response,
        int attempt,
        CancellationToken cancellationToken)
    {
        return retryPolicy.DelayBeforeRetryAsync(
            clientOptions.RetryBaseDelayMilliseconds,
            response,
            attempt,
            cancellationToken);
    }
}
