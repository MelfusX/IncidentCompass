namespace IncidentCompass.Application.Investigation.Jobs;

internal interface ITriageToolResultCommitFaultInjector
{
    Task AfterArtifactInsertedAsync(CancellationToken cancellationToken);
}
