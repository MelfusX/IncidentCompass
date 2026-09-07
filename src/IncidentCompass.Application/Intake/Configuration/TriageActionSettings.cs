namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageActionSettings(
    IReadOnlyCollection<string> AllowedTools,
    string DefaultMode,
    bool RequireApprovalForAll,
    int ApprovalTtlMinutes)
{
    public IReadOnlyList<IncidentCompass.Application.Notifications.NotificationRoute> NotificationRoutes { get; init; } = [];

    public static TriageActionSettings Default { get; } = new([], "disabled", false, 60);
}
