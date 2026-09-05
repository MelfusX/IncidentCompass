namespace IncidentCompass.Tester;

internal sealed record IncidentCorrelation(
    string TraceId,
    string SpanId,
    string ExternalId);
