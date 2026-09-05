namespace IncidentCompass.Application.Memory;

internal interface IMemoryRepository
{
    Task<IReadOnlyList<MemorySearchMatch>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken);

    Task<bool> SeedItemExistsAsync(
        string owner,
        MemorySeedItem item,
        CancellationToken cancellationToken);

    Task ReconcileSeedCorpusAsync(
        MemorySeedCorpus corpus,
        CancellationToken cancellationToken);
}
