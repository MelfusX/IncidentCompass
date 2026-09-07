namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionDecisionResult(
    ActionDecisionOutcome Outcome,
    ActionApprovalRecord? Action,
    string? ConflictCode);
