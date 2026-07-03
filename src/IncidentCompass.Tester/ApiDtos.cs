using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentCompass.Tester;

internal sealed record IngestSignalResponse(
    Guid SignalId,
    Guid FaultId,
    bool IsNewFault,
    bool IsNewJob,
    bool IsSuppressed,
    Guid? JobId,
    string? ConfigHash);

internal sealed record FaultLedgerResponse(Guid FaultId, IReadOnlyList<FaultLedgerEvent> Events);

internal sealed record FaultLedgerEvent(
    long Id,
    Guid JobId,
    int Attempt,
    string EventType,
    string? Role,
    string? ToolName,
    string? Decision,
    string? Rationale,
    string? PayloadRef,
    string ConfigHash);

internal sealed record TriageReportResponse(
    Guid Id,
    Guid FaultId,
    string Status,
    string Summary,
    string Classification,
    string Confidence,
    bool? IsMassIssue,
    string RecommendedNextAction,
    IReadOnlyList<string> Limitations,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TriageEvidenceResponse> Evidence);

internal sealed record TriageEvidenceResponse(
    Guid Id,
    string Kind,
    Guid ArtifactId,
    string Reference,
    string? Quote,
    double? Score,
    string ArtifactKind,
    string? ArtifactDomainRef,
    JsonElement ArtifactPayload);

[JsonSerializable(typeof(IncidentEnvelope))]
[JsonSerializable(typeof(IngestSignalResponse))]
[JsonSerializable(typeof(FaultLedgerResponse))]
[JsonSerializable(typeof(TriageReportResponse))]
internal sealed partial class TesterJsonContext : JsonSerializerContext;