namespace IncidentCompass.Infrastructure.Memory;

public interface IMemorySeedSyncStatus
{
    MemorySeedSyncSnapshot Snapshot { get; }
}
