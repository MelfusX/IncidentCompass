namespace IncidentCompass.IntegrationTests;

internal sealed record ActionApprovalOriginFixture(
    string TenantId,
    Guid SignalId,
    Guid FaultId,
    Guid JobId,
    Guid ReportId,
    Guid EvidenceArtifactId,
    string ConfigHash);
