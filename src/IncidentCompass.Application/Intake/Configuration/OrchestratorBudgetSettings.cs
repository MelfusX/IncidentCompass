namespace IncidentCompass.Application.Intake.Configuration;

public sealed record OrchestratorBudgetSettings(
    int MaxWorkers,
    int MaxTokens,
    int MaxWallClockSeconds,
    int MaxReprompts = 1);
