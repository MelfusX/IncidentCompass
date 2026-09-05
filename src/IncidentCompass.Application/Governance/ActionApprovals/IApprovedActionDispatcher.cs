namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IApprovedActionDispatcher
{
    Task SweepAsync(int batchSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionDispatchCandidate>> FindCandidatesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<ActionDispatchClaim?> TryClaimAsync(
        Guid actionId,
        string dispatchOwner,
        TimeSpan adapterTimeout,
        CancellationToken cancellationToken);

    Task DispatchAsync(
        ActionDispatchClaim claim,
        TimeSpan adapterTimeout,
        CancellationToken cancellationToken);
}
