namespace IncidentCompass.Application.Governance.ActionApprovals.Get;

public sealed record ActionApprovalProvenanceResponse(
    string SourceType,
    Guid SourceId,
    string? ArtifactKind,
    string TrustClass);
