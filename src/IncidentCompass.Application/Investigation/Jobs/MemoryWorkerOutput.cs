namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record MemoryWorkerOutput(
    bool Matched,
    IReadOnlyList<MemoryWorkerOutputItem> Items,
    string? NoMatchReason)
{
    public string Rationale => Matched
        ? "Memory returned " + Items.Count + " retrieved item(s)."
        : "Memory returned no matches" + (string.IsNullOrWhiteSpace(NoMatchReason) ? "." : ": " + NoMatchReason);
}
