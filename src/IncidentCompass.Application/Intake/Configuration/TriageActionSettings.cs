namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageActionSettings(
    IReadOnlyCollection<string> AllowedTools,
    string DefaultMode,
    bool RequireApprovalForAll,
    int ApprovalTtlMinutes)
{
    public static TriageActionSettings Default { get; } = new([], "disabled", false, 60);
}
