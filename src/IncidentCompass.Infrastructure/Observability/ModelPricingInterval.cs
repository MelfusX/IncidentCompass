namespace IncidentCompass.Infrastructure.Observability;

internal sealed record ModelPricingInterval(
    Guid Id,
    string Provider,
    string Model,
    string Currency,
    decimal InputTokenPricePerMillion,
    decimal OutputTokenPricePerMillion,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc)
{
    public bool Contains(DateTimeOffset timestampUtc) =>
        EffectiveFromUtc <= timestampUtc &&
        (EffectiveToUtc is null || EffectiveToUtc > timestampUtc);
}
