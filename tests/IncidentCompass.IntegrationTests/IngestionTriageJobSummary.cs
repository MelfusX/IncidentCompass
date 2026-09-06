namespace IncidentCompass.IntegrationTests;

internal sealed record IngestionTriageJobSummary(
    Guid Id,
    string Status,
    int Attempt,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc);
