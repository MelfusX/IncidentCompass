namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageRuleSettings(
    string Type,
    string Tool,
    string Scope,
    int? Max,
    string? RequiresSuccessfulToolResult);
