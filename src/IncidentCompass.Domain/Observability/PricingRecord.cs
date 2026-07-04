namespace IncidentCompass.Domain.Observability;

/// DORMANT: reserved for IC-BL-014 cost rollup work.
public sealed record PricingRecord(
    Guid Id,
    string Provider,
    string Model,
    string Currency,
    decimal InputTokenPricePerMillion,
    decimal OutputTokenPricePerMillion,
    decimal? EmbeddingTokenPricePerMillion,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc);
