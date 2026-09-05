using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record GovernedActionProposal(
    string TenantId,
    Guid OriginReportId,
    AgentToolDescriptor RegisteredTool,
    string ProposalKey,
    string AdapterBindingFingerprint,
    byte[] CanonicalPayload,
    string ReviewSummary,
    int ApprovalTtlMinutes,
    IReadOnlyList<Guid> EvidenceArtifactIds,
    TriageConfiguration Configuration)
{
    public string ToolId => RegisteredTool.ToolId;
}
