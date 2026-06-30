using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Application.Generation.ModelGateway;

public interface IAiModelRequestLogger
{
    Task<AiModelResponse> CompleteAndLogAsync(
        IAiModelClient modelClient,
        AiModelRequest request,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel,
        CancellationToken cancellationToken);

    Task LogSucceededWithoutModelAsync(
        string correlationId,
        string model,
        TimeSpan latency,
        int? embeddingTokens,
        string? embeddingProvider,
        string? embeddingModel);
}
