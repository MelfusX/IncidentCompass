using IncidentCompass.Application.Governance.ActionApprovals;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed record GroundedActionProposalContext(
    Guid FaultId,
    Guid JobId,
    int Attempt,
    string ConfigHash,
    IReadOnlyList<ActionApprovalProvenance> Provenance);
