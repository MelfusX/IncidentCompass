using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.Artifacts;

public interface ITriageArtifactRepository
{
    Task InsertAsync(TriageArtifact artifact, CancellationToken cancellationToken);

    Task ReplaceJobLevelAsync(TriageArtifact artifact, CancellationToken cancellationToken);
}
