using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

public interface ITriageJobRuntimeRepository
{
    Task<TriageJob?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task RecordAttemptFailureAsync(
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        CancellationToken cancellationToken);
}
