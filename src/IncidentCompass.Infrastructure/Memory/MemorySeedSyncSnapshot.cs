namespace IncidentCompass.Infrastructure.Memory;

public sealed record MemorySeedSyncSnapshot(
    bool Enabled,
    bool RuntimeResyncEnabled,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    Guid? ActiveGeneration,
    string? LastErrorCode);