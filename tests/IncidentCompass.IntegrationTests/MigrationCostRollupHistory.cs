namespace IncidentCompass.IntegrationTests;

internal sealed record MigrationCostRollupHistory(
    Guid PriceId,
    Guid JobId,
    string RouteId);
