namespace IncidentCompass.Application.Governance.PostReportActions;

public interface IPostReportActionIntentRepository
{
    Task<IReadOnlyList<PostReportActionIntentCandidate>> FindCandidatesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<PostReportActionIntentClaim?> TryClaimAsync(
        Guid intentId,
        string claimOwner,
        TimeSpan leaseDuration,
        int maximumAttempts,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid intentId,
        string claimOwner,
        Guid claimFence,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid intentId,
        Guid claimFence,
        string? resultCode,
        CancellationToken cancellationToken);

    Task<bool> RetryAsync(
        Guid intentId,
        Guid claimFence,
        string errorCode,
        TimeSpan retryDelay,
        int maximumAttempts,
        CancellationToken cancellationToken);

    Task<bool> DeadLetterAsync(
        Guid intentId,
        Guid claimFence,
        string errorCode,
        CancellationToken cancellationToken);
}
