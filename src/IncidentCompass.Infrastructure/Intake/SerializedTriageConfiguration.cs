using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed record SerializedTriageConfiguration(
    IReadOnlyDictionary<string, TriageProviderSettings>? Providers,
    IReadOnlyDictionary<string, TriageRouteSettings>? Routes,
    OrchestratorSettings? Orchestrator,
    IReadOnlyDictionary<string, TriageRoleSettings>? Roles,
    IReadOnlyDictionary<string, TriageToolSettings>? Tools,
    IReadOnlyCollection<TriageRuleSettings>? Rules,
    IngestionSettings? Ingestion,
    FaultGroupingSettings? FaultGrouping);
