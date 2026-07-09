namespace IncidentCompass.Infrastructure.Memory;

internal sealed record MemorySeedFrontmatter(
    string? Kind,
    string? ServiceName,
    string? Component,
    string? ReleaseName,
    IReadOnlyList<string> Tags);
