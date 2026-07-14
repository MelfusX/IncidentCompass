namespace IncidentCompass.Application.Intake.Configuration;

public sealed record SuppressionRuleSettings(
    string Id,
    int SilenceWindowMinutes,
    string? ServiceName = null,
    string? Severity = null);