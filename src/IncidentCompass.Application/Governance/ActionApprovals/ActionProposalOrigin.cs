using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionProposalOrigin(
    string TenantId,
    Guid ReportId,
    TriageJob Job,
    bool IsCompleted,
    IReadOnlyList<Guid> EvidenceArtifactIds);
