using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

internal static class FaultGroupingMetrics
{
    public static Task<int> CountNeighborsAsync(
        ISignalRepository signalRepository,
        Signal signal,
        FaultGroupingSettings settings,
        CancellationToken cancellationToken)
    {
        var windowStart = signal.ObservedAtUtc.AddMinutes(-settings.LookbackMinutes);
        return signalRepository.CountDistinctNeighborsAsync(
            signal.TenantId,
            signal.ServiceName,
            signal.Environment,
            signal.Fingerprint!,
            signal.FingerprintVersion!.Value,
            signal.GroupingRuleId,
            signal.GroupingRuleVersion,
            windowStart,
            signal.ObservedAtUtc,
            cancellationToken);
    }

    public static bool? DetermineIsMassIssue(Signal signal, int neighborCount, FaultGroupingSettings settings)
    {
        return signal.CanGroup
            ? neighborCount >= settings.MassIssue.MinNeighborCount &&
                signal.FingerprintStrength >= settings.MassIssue.GetMinimumFingerprintStrength()
            : null;
    }
}
