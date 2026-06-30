using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Infrastructure.Observability.Logging;

internal sealed record AiRequestLogWriteRequest(
    string CorrelationId,
    string Provider,
    string Model,
    string Status,
    string? ErrorCode,
    TimeSpan Latency,
    AiModelUsage? Usage,
    int? EmbeddingTokens,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    DateTimeOffset CreatedAtUtc);
