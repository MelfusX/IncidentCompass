namespace IncidentCompass.Infrastructure.Memory;

internal sealed record MemorySeedFile(
    string Kind,
    string Source,
    string Title,
    string Content,
    IReadOnlyList<string> Tags,
    string? ServiceName,
    string? Component,
    string? ReleaseName);
