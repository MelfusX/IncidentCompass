using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Governance.Tools;

internal sealed record ToolRulePolicyResult(
    TriageLedgerDecision Decision,
    string Reason,
    ActionExecutionMode? EffectiveMode = null)
{
    public bool MayProceed => Decision != TriageLedgerDecision.Denied;

    public static ToolRulePolicyResult Allowed(
        string reason,
        ActionExecutionMode? mode = null) =>
        new(TriageLedgerDecision.Allowed, reason, mode);

    public static ToolRulePolicyResult Denied(string reason) =>
        new(TriageLedgerDecision.Denied, reason);

    public static ToolRulePolicyResult ApprovalRequired(
        string reason,
        ActionExecutionMode? mode = null) =>
        new(TriageLedgerDecision.ApprovalRequired, reason, mode);
}
