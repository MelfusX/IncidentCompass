using System.Net;
using System.Text.Json;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi;

internal sealed class OpenAiEmbeddingErrorMapper
{
    public EmbeddingClientException FromHttpFailure(
        HttpStatusCode statusCode,
        string responseContent)
    {
        var providerError = OpenAiCompatibleErrorMapper.TryReadError(responseContent);
        return new EmbeddingClientException(
            OpenAiEmbeddingProvider.Name,
            providerError?.Error?.Message
                ?? $"Embedding provider returned HTTP {(int)statusCode}.",
            OpenAiCompatibleErrorMapper.NormalizeProviderErrorCode(statusCode),
            statusCode,
            providerError?.Error?.Code);
    }

    public EmbeddingClientException EmptyEmbedding()
    {
        return new EmbeddingClientException(
            OpenAiEmbeddingProvider.Name,
            "Embedding provider returned no embedding vector.",
            errorCode: "empty_embedding");
    }

    public EmbeddingClientException Timeout(TaskCanceledException exception)
    {
        return new EmbeddingClientException(
            OpenAiEmbeddingProvider.Name,
            "Embedding provider request timed out.",
            errorCode: "timeout",
            innerException: exception);
    }

    public EmbeddingClientException Transport(HttpRequestException exception)
    {
        return new EmbeddingClientException(
            OpenAiEmbeddingProvider.Name,
            "Embedding provider request failed before a valid response was received.",
            errorCode: "transport_error",
            statusCode: exception.StatusCode,
            innerException: exception);
    }

    public EmbeddingClientException InvalidJson(JsonException exception)
    {
        return new EmbeddingClientException(
            OpenAiEmbeddingProvider.Name,
            "Embedding provider returned an invalid JSON response.",
            errorCode: "invalid_json",
            innerException: exception);
    }
}
