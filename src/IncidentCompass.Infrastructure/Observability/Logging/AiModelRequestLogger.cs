using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.ModelGateway;

namespace IncidentCompass.Infrastructure.Observability.Logging;

internal sealed class AiModelRequestLogger(
    AiModelRequestLoggingService requestLoggingService)
    : IAiModelRequestLogger
{
    public Task<AiModelResponse> CompleteAndLogAsync(
        IAiModelClient modelClient,
        AiModelRequest request,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel,
        CancellationToken cancellationToken)
    {
        return requestLoggingService.CompleteAndLogAsync(
            modelClient,
            request,
            embeddingTokens,
            embeddingProvider,
            embeddingModel,
            cancellationToken);
    }

    public Task LogSucceededWithoutModelAsync(
        string correlationId,
        string model,
        TimeSpan latency,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel)
    {
        return requestLoggingService.LogSucceededWithoutModelAsync(
            correlationId,
            model,
            latency,
            embeddingTokens,
            embeddingProvider,
            embeddingModel);
    }
}
