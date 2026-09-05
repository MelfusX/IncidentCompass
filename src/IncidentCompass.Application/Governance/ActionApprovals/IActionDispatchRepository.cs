namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IActionDispatchRepository
{
    Task<IReadOnlyList<ActionDispatchCandidate>> FindCandidatesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionDispatchCandidate>> FindExpiryCandidatesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionDispatchCandidate>> FindSupersededCandidatesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<ActionDispatchClaim?> TryClaimAsync(
        Guid actionId,
        string dispatchOwner,
        TimeSpan timeoutWithRecoveryGrace,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        ActionTerminalRequest request,
        CancellationToken cancellationToken);

    Task<bool> TryExpireAsync(Guid actionId, CancellationToken cancellationToken);

    Task<bool> TryFailSupersededAsync(Guid actionId, CancellationToken cancellationToken);
}
