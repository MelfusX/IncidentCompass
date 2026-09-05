using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Worker;

public sealed class WorkerJobLeaseRenewer
{
    private static readonly TimeSpan MinimumRenewalInterval = TimeSpan.FromMilliseconds(100);

    public async Task<bool> RenewUntilStoppedAsync(
        ITriageJobRunner runner,
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var interval = CalculateRenewalInterval(leaseDuration);
        while (true)
        {
            await Task.Delay(interval, cancellationToken);
            if (!await runner.RenewLeaseAsync(job, workerId, leaseDuration, cancellationToken))
            {
                return false;
            }
        }
    }

    private static TimeSpan CalculateRenewalInterval(TimeSpan leaseDuration)
    {
        var oneThird = TimeSpan.FromTicks(leaseDuration.Ticks / 3);
        return oneThird >= MinimumRenewalInterval ? oneThird : MinimumRenewalInterval;
    }
}
