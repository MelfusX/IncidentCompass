namespace IncidentCompass.Application.Intake.Configuration;

public sealed record OrchestratorSettings(
    string Instructions,
    string RouteId,
    IReadOnlyCollection<string> Tools,
    OrchestratorBudgetSettings Budget);
