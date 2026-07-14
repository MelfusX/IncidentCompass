using IncidentCompass.Infrastructure.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IncidentCompass.Api.Health;

internal sealed class MemorySeedSyncHealthCheck(IMemorySeedSyncStatusReader syncStatus) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await syncStatus.GetAsync(cancellationToken);
        var metadata = new Dictionary<string, object>
        {
            ["enabled"] = snapshot.Enabled,
            ["runtimeResyncEnabled"] = snapshot.RuntimeResyncEnabled,
            ["lastAttemptAtUtc"] = snapshot.LastAttemptAtUtc?.ToString("O") ?? string.Empty,
            ["lastSuccessAtUtc"] = snapshot.LastSuccessAtUtc?.ToString("O") ?? string.Empty,
            ["activeGeneration"] = snapshot.ActiveGeneration?.ToString() ?? string.Empty,
            ["lastErrorCode"] = snapshot.LastErrorCode ?? string.Empty
        };
        if (!snapshot.Enabled || string.IsNullOrWhiteSpace(snapshot.LastErrorCode))
        {
            return HealthCheckResult.Healthy("Memory seed synchronization is healthy.", metadata);
        }

        return HealthCheckResult.Degraded(
            "Memory seed synchronization failed; the previous corpus remains active.",
            data: metadata);
    }
}