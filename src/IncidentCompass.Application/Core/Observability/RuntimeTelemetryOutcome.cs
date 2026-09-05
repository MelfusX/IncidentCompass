namespace IncidentCompass.Application.Core.Observability;

public enum RuntimeTelemetryOutcome
{
    Claimed,
    Succeeded,
    Failed,
    Cancelled,
    ProviderUnavailable,
    Denied
}
