namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageConfiguration(
    string ConfigHash,
    IReadOnlyDictionary<string, TriageProviderSettings> Providers,
    IReadOnlyDictionary<string, TriageRouteSettings> Routes,
    OrchestratorSettings Orchestrator,
    IReadOnlyDictionary<string, TriageRoleSettings> Roles,
    IReadOnlyDictionary<string, TriageToolSettings> Tools,
    IReadOnlyCollection<TriageRuleSettings> Rules,
    IngestionSettings Ingestion,
    FaultGroupingSettings FaultGrouping,
    RedactionSettings Redaction);
