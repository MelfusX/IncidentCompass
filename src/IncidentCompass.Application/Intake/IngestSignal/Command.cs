using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Intake.IngestSignal;

public sealed record IngestSignalCommand(
    string SourceKind,
    string? ServiceName,
    string? Environment,
    string? Severity,
    string? Summary,
    string? Description,
    DateTimeOffset? ObservedAtUtc,
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    string? ExternalId,
    JsonNode? Attributes,
    JsonNode? Payload) : IRequest<IngestSignalResponse>;
