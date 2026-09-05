namespace IncidentCompass.Application.Observability.CostRollup;

public sealed record CostRollupHour(
    DateTimeOffset HourUtc,
    long CallCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long PricedCallCount,
    long UnpricedCallCount,
    IReadOnlyList<CostRollupSpend> SpendTotals);
