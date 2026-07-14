namespace IncidentCompass.Infrastructure.Memory;

internal interface IMemorySeedSyncStatusWriter
{
    Task SaveAsync(MemorySeedSyncSnapshot snapshot, CancellationToken cancellationToken);
}