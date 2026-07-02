using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record WorkerToolPolicyResult(
    TriageLedgerDecision Decision,
    string Reason)
{
    public bool MayExecute => Decision == TriageLedgerDecision.Allowed;

    public static WorkerToolPolicyResult Allowed(string reason)
    {
        return new WorkerToolPolicyResult(TriageLedgerDecision.Allowed, reason);
    }

    public static WorkerToolPolicyResult Denied(string reason)
    {
        return new WorkerToolPolicyResult(TriageLedgerDecision.Denied, reason);
    }

    public static WorkerToolPolicyResult ApprovalRequired(string reason)
    {
        return new WorkerToolPolicyResult(TriageLedgerDecision.ApprovalRequired, reason);
    }
}