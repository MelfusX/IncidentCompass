namespace IncidentCompass.Infrastructure.Investigation;

internal sealed record GroundedReportEvidence(
    Guid ArtifactId,
    string Kind,
    string Reference,
    string? Quote,
    double? Score,
    Guid? MemoryItemId,
    string? DocumentationStatus);