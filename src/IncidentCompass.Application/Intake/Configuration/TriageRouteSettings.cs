namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageRouteSettings(
    string Kind,
    string ProviderId,
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? ContextWindowTokens);
