using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record PreparedActionProposal(
    string TenantId,
    Guid OriginReportId,
    string ToolId,
    string ProposalKey,
    ActionCategory Category,
    ActionExecutionMode Mode,
    string LogicalTargetId,
    string AdapterBindingFingerprint,
    byte[] CanonicalPayload,
    string ReviewSummary,
    int ApprovalTtlMinutes,
    IReadOnlyList<Guid> EvidenceArtifactIds,
    bool AutomaticallyApproved,
    string PolicyDecisionReason);
