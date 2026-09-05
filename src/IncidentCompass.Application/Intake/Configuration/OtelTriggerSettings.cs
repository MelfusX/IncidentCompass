namespace IncidentCompass.Application.Intake.Configuration;

public sealed record OtelTriggerSettings(
    bool ErrorsOnly,
    IReadOnlyCollection<string> ServiceAllowList,
    IReadOnlyCollection<string> SeverityAllowList)
{
    public static OtelTriggerSettings Default { get; } = new(
        ErrorsOnly: true,
        ServiceAllowList: [],
        SeverityAllowList: []);
}
