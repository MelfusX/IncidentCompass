using System.Text.Json;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Infrastructure.Embeddings.OpenAi.Dtos;

namespace IncidentCompass.Infrastructure.Embeddings.OpenAi;

internal sealed class OpenAiEmbeddingResponseMapper
{
    public EmbeddingResponse Map(
        string responseContent,
        EmbeddingRequest request,
        OpenAiEmbeddingErrorMapper errorMapper)
    {
        var embeddingResponse = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(
            responseContent,
            OpenAiEmbeddingJson.Options);
        var data = embeddingResponse?.Data;
        var embedding = data is { Count: > 0 } && data[0] is { } item ? item.Embedding : null;
        if (embedding is null || embedding.Count == 0)
        {
            throw errorMapper.EmptyEmbedding();
        }

        return new EmbeddingResponse(
            embedding,
            embeddingResponse?.Model ?? request.Model,
            OpenAiEmbeddingProvider.Name,
            embeddingResponse?.Usage?.PromptTokens,
            request.CorrelationId);
    }
}
