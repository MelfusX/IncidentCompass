using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Domain.Incidents;

public sealed record TriageLedgerEntry(
    long Id,
    Guid FaultId,
    Guid JobId,
    int Attempt,
    TriageLedgerEventType EventType,
    string? Role,
    string? ToolName,
    string? Rationale,
    TriageLedgerDecision? Decision,
    string? DecisionReason,
    string? PayloadRef,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    TriageLedgerToolStatus? ToolStatus = null,
    int? TokensDelta = null,
    int? WorkersDelta = null);