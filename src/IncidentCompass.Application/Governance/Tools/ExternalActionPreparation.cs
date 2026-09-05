namespace IncidentCompass.Application.Governance.Tools;

public sealed record ExternalActionPreparation(
    byte[] CanonicalPayload,
    string ReviewSummary);
