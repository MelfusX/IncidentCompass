namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageRoleSettings(
    string RouteId,
    string Instructions,
    IReadOnlyCollection<string> Tools,
    string OutputSchema);
