namespace IncidentCompass.Application.Memory;

internal sealed record MemorySeedCorpus(
    string TenantId,
    string Owner,
    Guid Generation,
    IReadOnlySet<string> ObservedSourcePrefixes,
    IReadOnlyList<MemorySeedEntry> Entries);