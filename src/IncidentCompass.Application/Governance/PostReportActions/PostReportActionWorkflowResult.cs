namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed record PostReportActionWorkflowResult(
    bool IsCompleted,
    bool ShouldRetry,
    string Code)
{
    public static PostReportActionWorkflowResult Completed(string code = "completed") =>
        new(true, false, code);

    public static PostReportActionWorkflowResult Retry(string code) =>
        new(false, true, code);

    public static PostReportActionWorkflowResult DeadLetter(string code) =>
        new(false, false, code);
}
