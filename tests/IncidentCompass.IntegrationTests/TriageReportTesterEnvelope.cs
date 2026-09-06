namespace IncidentCompass.IntegrationTests;

internal sealed record TriageReportTesterEnvelope(
    string SourceKind,
    string ServiceName,
    string Environment,
    DateTimeOffset ObservedAtUtc,
    TriageReportTesterAttributes Attributes);
