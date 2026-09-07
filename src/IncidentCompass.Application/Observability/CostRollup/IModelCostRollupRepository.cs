namespace IncidentCompass.Application.Observability.CostRollup;

public interface IModelCostRollupRepository
{
    Task<IReadOnlyList<CostRollupHour>> ReadAsync(
        string tenantId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
