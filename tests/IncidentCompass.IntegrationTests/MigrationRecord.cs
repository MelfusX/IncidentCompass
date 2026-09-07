namespace IncidentCompass.IntegrationTests;

internal sealed record MigrationRecord(
    int Version,
    string Name,
    string Checksum,
    string Status,
    DateTimeOffset? AppliedAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? ErrorMessage);
