using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed record TriageReportEvidenceResponse(
    Guid Id,
    string Kind,
    Guid ArtifactId,
    string Reference,
    string? Quote,
    double? Score,
    string ArtifactKind,
    string? ArtifactDomainRef,
    JsonElement ArtifactPayload);
