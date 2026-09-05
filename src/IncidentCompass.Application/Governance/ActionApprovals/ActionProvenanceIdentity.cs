using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionProvenanceIdentity(Guid ArtifactId, ActionProvenanceTrust TrustClass);
