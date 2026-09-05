namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionDispatchClaim(ActionApprovalRecord Action, Guid Fence);
