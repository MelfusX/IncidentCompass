using IncidentCompass.Domain.Observability;

namespace IncidentCompass.Infrastructure.Observability;

public interface IPricingRepository
{
    Task<PricingRecord?> GetEffectivePricingAsync(
        string provider,
        string model,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken);
}
