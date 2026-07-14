using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

public interface ITriageJobRunner
{
    Task<TriageJob?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task ProcessClaimedAsync(
        TriageJob job,
        string workerId,
        TriageJobProcessingSettings settings,
        CancellationToken cancellationToken);
}