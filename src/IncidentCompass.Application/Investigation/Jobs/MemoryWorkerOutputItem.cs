namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record MemoryWorkerOutputItem(
    string ArtifactId,
    string Title,
    string Quote,
    double? Score);
