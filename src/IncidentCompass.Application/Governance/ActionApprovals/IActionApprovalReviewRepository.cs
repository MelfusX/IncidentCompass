namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IActionApprovalReviewRepository
{
    Task<IReadOnlyList<ActionApprovalRecord>> ListAsync(
        ActionApprovalListFilter filter,
        string tenantId,
        CancellationToken cancellationToken);

    Task<(ActionApprovalRecord Action, IReadOnlyList<ActionApprovalProvenance> Provenance)?> FindAsync(
        Guid actionId,
        string tenantId,
        CancellationToken cancellationToken);

    Task<ActionDecisionResult> DecideAsync(
        ActionDecisionRequest request,
        CancellationToken cancellationToken);
}
