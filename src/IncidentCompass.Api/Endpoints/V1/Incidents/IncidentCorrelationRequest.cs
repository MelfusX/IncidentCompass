namespace IncidentCompass.Api;

public sealed record IncidentCorrelationRequest(string? TraceId, string? SpanId, string? ExternalId);
