namespace IncidentCompass.Application.Governance.PostReportActions;

public enum PostReportActionIntentState
{
    Pending,
    Processing,
    RetryPending,
    Completed,
    DeadLettered
}
