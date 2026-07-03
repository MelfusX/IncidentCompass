using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.UnitTests;

internal static class TestTriageConfiguration
{
    public static TriageConfiguration Create(
        string configHash = "config-hash-1",
        IReadOnlyCollection<string>? allowedSources = null,
        int silenceWindowMinutes = 30,
        int lookbackMinutes = 15,
        int minNeighborCount = 5,
        string minFingerprintStrength = "strong") => new(
        ConfigHash: configHash,
        Providers: new Dictionary<string, TriageProviderSettings>(StringComparer.Ordinal)
        {
            ["local-oai"] = new("OpenAICompatible", "http://localhost:1234/v1", "LOCAL_OAI_KEY")
        },
        Routes: new Dictionary<string, TriageRouteSettings>(StringComparer.Ordinal)
        {
            ["analysis-chat"] = new("Chat", "local-oai", "local-model", 0.1, 2000, 8192),
            ["report-chat"] = new("Chat", "local-oai", "local-model", 0.2, 4000, 8192),
            ["memory-embed"] = new("Embedding", "local-oai", "mock-memory-embedding-v1", null, null, null)
        },
        Orchestrator: new OrchestratorSettings(
            "orchestrator instructions",
            "report-chat",
            ["delegate", "publish_report"],
            new OrchestratorBudgetSettings(6, 200000, 120)),
        Roles: new Dictionary<string, TriageRoleSettings>(StringComparer.Ordinal)
        {
            ["analysis"] = new("analysis-chat", "analysis instructions", [], "analysis schema"),
            ["memory"] = new("analysis-chat", "memory instructions", ["memory_search"], "memory schema")
        },
        Tools: new Dictionary<string, TriageToolSettings>(StringComparer.Ordinal)
        {
            ["memory_search"] = new("internal", "memory-embed", 5, 0.25)
        },
        Rules: [new TriageRuleSettings("rate_cap", "*", "attempt", 50, null)],
        Ingestion: new IngestionSettings(
            "local",
            allowedSources ?? [
                SignalSourceKinds.Tester,
                SignalSourceKinds.User,
                SignalSourceKinds.Manual,
                SignalSourceKinds.Otel
            ]),
        FaultGrouping: new FaultGroupingSettings(
            lookbackMinutes,
            silenceWindowMinutes,
            1,
            new MassIssueSettings(minNeighborCount, minFingerprintStrength)));
}
