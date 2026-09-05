namespace IncidentCompass.Tester;

internal sealed record IncidentEnvelope(
    string SourceKind,
    string ServiceName,
    string Environment,
    string Severity,
    DateTimeOffset ObservedAtUtc,
    IncidentCorrelation Correlation,
    IncidentAttributes Attributes,
    IReadOnlyDictionary<string, object?> Payload);
