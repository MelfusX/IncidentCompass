namespace IncidentCompass.Domain.Incidents.Actions;

public enum ActionApprovalState
{
    Requested,
    Approved,
    Rejected,
    Expired,
    Executed,
    Failed
}
