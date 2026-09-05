namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class NoopTriageToolResultCommitFaultInjector : ITriageToolResultCommitFaultInjector
{
    public Task AfterArtifactInsertedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
