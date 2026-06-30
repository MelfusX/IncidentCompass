namespace IncidentCompass.Domain.Observability;

public sealed record AiRequestLogEntry(
    Guid RequestId,
    string ApiVersion,
    string? UserId,
    string? TenantId,
    string CorrelationId,
    string Provider,
    string Model,
    string Status,
    string? ErrorCode,
    TimeSpan Latency,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    int? EmbeddingTokens,
    decimal? EstimatedCost,
    string? CostCurrency,
    DateTimeOffset CreatedAtUtc);
