namespace IncidentCompass.Infrastructure.Memory;

internal sealed record MemorySeedScan(
    IReadOnlyList<MemorySeedFile> Files,
    IReadOnlySet<string> PresentDirectories);
