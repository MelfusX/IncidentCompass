namespace IncidentCompass.Application.Investigation.Reports.Context;

public sealed record ReadOnlyContextOutcome(
    string ToolName,
    ReadOnlyContextOutcomeStatus Status,
    string Code);
