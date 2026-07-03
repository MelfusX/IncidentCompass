namespace IncidentCompass.Application.Memory;

internal interface IMemoryRepository
{
    Task<IReadOnlyList<MemorySearchMatch>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken);

    Task UpsertSeedAsync(
        MemorySeedItem item,
        IReadOnlyList<MemorySeedChunk> chunks,
        CancellationToken cancellationToken);
}
