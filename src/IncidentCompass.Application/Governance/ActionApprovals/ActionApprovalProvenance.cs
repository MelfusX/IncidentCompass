using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionApprovalProvenance(
    int Ordinal,
    string SourceType,
    Guid SourceId,
    string? ArtifactKind,
    ActionProvenanceTrust TrustClass);
