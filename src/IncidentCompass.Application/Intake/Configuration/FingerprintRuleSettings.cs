namespace IncidentCompass.Application.Intake.Configuration;

public sealed record FingerprintRuleSettings(
    string Id,
    int Version,
    IReadOnlyCollection<string> Inputs,
    string? ServiceName = null,
    string? OperationName = null,
    string? Source = null);
