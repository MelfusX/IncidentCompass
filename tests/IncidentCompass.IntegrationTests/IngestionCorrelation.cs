namespace IncidentCompass.IntegrationTests;

internal sealed record IngestionCorrelation(string? TraceId, string? SpanId, string? ExternalId);
