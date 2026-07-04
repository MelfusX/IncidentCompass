namespace IncidentCompass.Domain.Governance;

/// DORMANT: reserved for IC-BL-010 governed tool execution work.
public sealed record ToolPolicyDecision(
    ToolRisk Risk,
    string Decision,
    string Reason,
    bool RequiresApproval,
    bool MayExecute);
