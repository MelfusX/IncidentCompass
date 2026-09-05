namespace IncidentCompass.Application.Governance.Tools;

public sealed record ExternalActionExecutionResult(
    bool Succeeded,
    byte[] CanonicalResult,
    string Summary,
    string? FailureCode = null);
