using System.Text.Json.Nodes;

namespace IncidentCompass.Api;

public sealed record IncidentEnvelopeRequest(
    string SourceKind,
    string? ServiceName,
    string? Environment,
    string? Severity,
    string? Summary,
    string? Description,
    DateTimeOffset? ObservedAtUtc,
    IncidentCorrelationRequest? Correlation,
    JsonNode? Attributes,
    JsonNode? Payload);
