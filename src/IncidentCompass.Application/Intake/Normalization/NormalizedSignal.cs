using System.Text.Json.Nodes;

namespace IncidentCompass.Application.Intake.Normalization;

// Uses JsonNode, not JsonElement, because redaction needs to mutate/rebuild the tree --
// conversion to JsonElement happens later, in the IngestSignal handler, right before
// constructing the Domain Signal.
public sealed record NormalizedSignal(
    string Source,
    string? ExternalId,
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    string ServiceName,
    string Environment,
    string? OperationName,
    string? Severity,
    string? ErrorType,
    string? ErrorMessage,
    string Summary,
    string? Description,
    string? HttpMethod,
    string? HttpRoute,
    int? HttpStatusCode,
    int? DurationMs,
    JsonNode Attributes,
    JsonNode Body,
    DateTimeOffset ObservedAtUtc);
