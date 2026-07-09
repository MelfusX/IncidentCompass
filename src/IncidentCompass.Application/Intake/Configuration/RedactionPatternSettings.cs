namespace IncidentCompass.Application.Intake.Configuration;

public sealed record RedactionPatternSettings(
    string Name,
    string Pattern,
    string Replacement = "[REDACTED]",
    bool IgnoreCase = true);
