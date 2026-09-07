namespace IncidentCompass.Application.Observability.CostRollup;

public sealed record CostRollupSpend(
    string Currency,
    decimal Amount);
