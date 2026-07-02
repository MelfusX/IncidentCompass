namespace IncidentCompass.Application.Intake.Configuration;

public sealed record IngestionSettings(string DefaultTenant, IReadOnlyCollection<string> AllowedSources);
