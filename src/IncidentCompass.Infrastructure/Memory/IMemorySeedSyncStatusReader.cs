namespace IncidentCompass.Infrastructure.Memory;

public interface IMemorySeedSyncStatusReader
{
    Task<MemorySeedSyncSnapshot> GetAsync(CancellationToken cancellationToken);
}
