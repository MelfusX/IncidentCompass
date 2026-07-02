using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Application.Core.ModelGateway;

internal sealed class NoopAiModelRequestLogger : IAiModelRequestLogger
{
    public Task<AiModelResponse> CompleteAndLogAsync(
        IAiModelClient modelClient,
        AiModelRequest request,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel,
        CancellationToken cancellationToken)
    {
        return modelClient.CompleteAsync(request, cancellationToken);
    }

    public Task LogSucceededWithoutModelAsync(
        string correlationId,
        string model,
        TimeSpan latency,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel)
    {
        return Task.CompletedTask;
    }
}
