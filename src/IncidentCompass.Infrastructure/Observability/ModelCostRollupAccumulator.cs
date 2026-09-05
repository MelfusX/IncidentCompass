using IncidentCompass.Application.Observability.CostRollup;

namespace IncidentCompass.Infrastructure.Observability;

internal sealed class ModelCostRollupAccumulator(IReadOnlyList<ModelPricingInterval> prices)
{
    private const decimal TokensPerMillion = 1_000_000m;
    private readonly Dictionary<DateTimeOffset, MutableCostRollupHour> hours = [];
    private readonly IReadOnlyDictionary<(string Provider, string Model), ModelPricingInterval[]> pricesByIdentity =
        prices
            .GroupBy(static price => (price.Provider, price.Model))
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

    public void Add(DateTimeOffset createdAtUtc, string? rationale)
    {
        var hourUtc = new DateTimeOffset(
            createdAtUtc.Year,
            createdAtUtc.Month,
            createdAtUtc.Day,
            createdAtUtc.Hour,
            0,
            0,
            TimeSpan.Zero);
        if (!hours.TryGetValue(hourUtc, out var hour))
        {
            hour = new MutableCostRollupHour();
            hours.Add(hourUtc, hour);
        }

        hour.CallCount++;
        if (!ModelCallUsageParser.TryParse(rationale, out var usage))
        {
            hour.UnpricedCallCount++;
            return;
        }

        hour.InputTokens += usage!.InputTokens;
        hour.OutputTokens += usage.OutputTokens;
        hour.TotalTokens += usage.TotalTokens;
        var matchingPrices = pricesByIdentity.TryGetValue((usage.Provider, usage.Model), out var candidates)
            ? candidates.Where(price => price.Contains(createdAtUtc)).Take(2).ToArray()
            : [];
        if (matchingPrices.Length != 1)
        {
            hour.UnpricedCallCount++;
            return;
        }

        hour.PricedCallCount++;
        var price = matchingPrices[0];
        var amount =
            usage.InputTokens / TokensPerMillion * price.InputTokenPricePerMillion +
            usage.OutputTokens / TokensPerMillion * price.OutputTokenPricePerMillion;
        hour.SpendByCurrency[price.Currency] =
            hour.SpendByCurrency.GetValueOrDefault(price.Currency) + amount;
    }

    public IReadOnlyList<CostRollupHour> Build() =>
        hours
            .OrderBy(static item => item.Key)
            .Select(static item => item.Value.ToResponse(item.Key))
            .ToArray();
}
