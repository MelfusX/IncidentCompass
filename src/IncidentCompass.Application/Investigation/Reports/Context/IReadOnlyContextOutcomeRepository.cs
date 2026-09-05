namespace IncidentCompass.Application.Investigation.Reports.Context;

public interface IReadOnlyContextOutcomeRepository
{
    Task<IReadOnlyList<ReadOnlyContextOutcome>> ReadCurrentAttemptAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken);
}
