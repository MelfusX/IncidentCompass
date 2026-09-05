namespace IncidentCompass.Application.Governance.ActionApprovals;

public static class ActionProposalDenialReason
{
    public static string NormalizePolicy(string reason) => reason switch
    {
        "action_not_granted" => "action_not_granted",
        "action_registration_mismatch" => "tool_registration_mismatch",
        "action_disabled" => "action_disabled",
        _ when reason.StartsWith("rate_cap exceeded", StringComparison.Ordinal) => "rate_cap_exceeded",
        _ when reason.StartsWith("precondition unsatisfied", StringComparison.Ordinal) => "precondition_unsatisfied",
        _ => "policy_denied"
    };
}
