namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageProviderSettings(
    string Kind,
    string? Endpoint,
    string? ApiKeySecretRef);
