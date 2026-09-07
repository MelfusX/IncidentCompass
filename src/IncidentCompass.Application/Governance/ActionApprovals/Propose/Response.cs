namespace IncidentCompass.Application.Governance.ActionApprovals.Propose;

public sealed record PostReportActionProposalResponse(
    PostReportActionProposalOutcome Outcome,
    string ReasonCode,
    ActionApprovalRecord? Action,
    bool IsReplay,
    bool DenialAudited);
