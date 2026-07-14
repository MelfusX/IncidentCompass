namespace IncidentCompass.Infrastructure.Memory;

internal sealed class MemorySeedSyncStatus : IMemorySeedSyncStatus
{
    private readonly object gate = new();
    private MemorySeedSyncSnapshot snapshot = new(false, false, null, null, null, null);

    public MemorySeedSyncSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public void Configure(bool enabled, bool runtimeResyncEnabled)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                Enabled = enabled,
                RuntimeResyncEnabled = runtimeResyncEnabled
            };
        }
    }

    public void RecordAttempt(DateTimeOffset timestamp)
    {
        lock (gate)
        {
            snapshot = snapshot with { LastAttemptAtUtc = timestamp, LastErrorCode = null };
        }
    }

    public void RecordSuccess(DateTimeOffset timestamp, Guid generation)
    {
        lock (gate)
        {
            snapshot = snapshot with
            {
                LastSuccessAtUtc = timestamp,
                ActiveGeneration = generation,
                LastErrorCode = null
            };
        }
    }

    public void RecordFailure()
    {
        lock (gate)
        {
            snapshot = snapshot with { LastErrorCode = "memory_sync_failed" };
        }
    }
}