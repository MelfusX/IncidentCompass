namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionProposalResult(ActionApprovalRecord Action, bool IsReplay);
