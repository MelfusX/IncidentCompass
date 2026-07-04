using IncidentCompass.Domain.Observability;

namespace IncidentCompass.Infrastructure.Observability;

/// DORMANT: reserved for IC-BL-014 cost rollup work.
public interface IPricingRepository
{
    Task<PricingRecord?> GetEffectivePricingAsync(
        string provider,
        string model,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken);
}
