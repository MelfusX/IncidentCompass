namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed record ActionProposalGroundingInput(
    string TenantId,
    Guid OriginReportId,
    IReadOnlyList<Guid> EvidenceArtifactIds);
