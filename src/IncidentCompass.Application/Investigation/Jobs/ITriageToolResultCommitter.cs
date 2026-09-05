using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal interface ITriageToolResultCommitter
{
    Task<TriageArtifact> CommitSucceededAsync(
        TriageToolResultCommitRequest request,
        CancellationToken cancellationToken);
}
