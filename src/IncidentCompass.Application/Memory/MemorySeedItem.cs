namespace IncidentCompass.Application.Memory;

internal sealed record MemorySeedItem(
    Guid Id,
    string TenantId,
    string Kind,
    string Source,
    string Title,
    string Content,
    string ContentHash,
    int Version,
    IReadOnlyList<string> Tags,
    string? ServiceName,
    string? Component,
    string? ReleaseName);
