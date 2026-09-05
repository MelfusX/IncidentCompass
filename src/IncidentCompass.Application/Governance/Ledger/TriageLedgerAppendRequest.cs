using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Governance.Ledger;

public sealed record TriageLedgerAppendRequest(
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
    TriageLedgerToolStatus? ToolStatus = null,
    int? TokensDelta = null,
    int? WorkersDelta = null);
