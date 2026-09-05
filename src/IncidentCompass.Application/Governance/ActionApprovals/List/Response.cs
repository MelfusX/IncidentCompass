namespace IncidentCompass.Application.Governance.ActionApprovals.List;

public sealed record ActionApprovalListResponse(
    IReadOnlyList<ActionApprovalListItemResponse> Actions,
    string? NextCursor);
