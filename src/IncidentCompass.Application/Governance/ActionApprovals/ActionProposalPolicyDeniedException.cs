namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed class ActionProposalPolicyDeniedException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
