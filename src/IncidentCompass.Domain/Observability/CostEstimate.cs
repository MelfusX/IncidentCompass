namespace IncidentCompass.Domain.Observability;

public sealed record CostEstimate(
    decimal Amount,
    string Currency,
    Guid PricingRecordId);
