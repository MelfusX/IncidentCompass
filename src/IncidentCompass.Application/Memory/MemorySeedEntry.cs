namespace IncidentCompass.Application.Memory;

internal sealed record MemorySeedEntry(
    MemorySeedItem Item,
    IReadOnlyList<MemorySeedChunk> Chunks);
