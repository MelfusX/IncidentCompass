namespace IncidentCompass.IntegrationTests;

internal sealed record IngestionTesterEnvelope(
    string SourceKind,
    string ServiceName,
    string Environment,
    DateTimeOffset ObservedAtUtc,
    IngestionTesterAttributes Attributes,
    IngestionCorrelation? Correlation = null);
