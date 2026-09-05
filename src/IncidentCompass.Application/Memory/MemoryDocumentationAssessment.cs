namespace IncidentCompass.Application.Memory;

internal sealed record MemoryDocumentationAssessment(
    string? TargetCurrentRelease,
    MemoryDocumentationStatus Status);
