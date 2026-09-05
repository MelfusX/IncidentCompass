namespace IncidentCompass.Application.Observability.CostRollup;

public sealed record CostRollupResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<CostRollupHour> Hours);
