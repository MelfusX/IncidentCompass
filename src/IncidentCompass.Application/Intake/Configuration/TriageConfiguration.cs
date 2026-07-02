namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageConfiguration(string ConfigHash, IngestionSettings Ingestion, FaultGroupingSettings FaultGrouping);
