namespace IncidentCompass.Application.Investigation.Jobs.Testing;

/// <summary>
/// Production binding for <see cref="ITriageToolResultCommitFaultInjector"/>. It never faults; the
/// consuming committer needs something to call at the seam.
/// </summary>
internal sealed class NoopTriageToolResultCommitFaultInjector : ITriageToolResultCommitFaultInjector
{
    public Task AfterArtifactInsertedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
