namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionDecisionRequest(
    Guid ActionId,
    string TenantId,
    string Actor,
    ActionDecisionKind Decision,
    string PayloadSha256,
    string ApprovalSha256,
    string? RejectionReason);
