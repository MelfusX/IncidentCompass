using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed record ActionTerminalRequest(
    Guid ActionId,
    Guid DispatchFence,
    ActionApprovalState TerminalState,
    byte[] ResultPayload,
    string ResultSummary,
    string? FailureCode,
    ExternalActionAuditProjection? AuditProjection = null);
