using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Notifications;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.IntegrationTests;

internal static class ActionDispatchTestConfiguration
{
    public static TriageConfiguration Create(
        ActionExecutionMode mode = ActionExecutionMode.Live,
        bool requireApproval = false,
        bool allowed = true,
        string toolId = "action_test") =>
        new(
            "action-dispatch-current",
            new Dictionary<string, TriageProviderSettings>(StringComparer.Ordinal),
            new Dictionary<string, TriageRouteSettings>(StringComparer.Ordinal),
            new OrchestratorSettings(
                "orchestrator",
                "chat",
                ["delegate", "publish_report"],
                new OrchestratorBudgetSettings(1, 1000, 30)),
            new Dictionary<string, TriageRoleSettings>(StringComparer.Ordinal),
            new Dictionary<string, TriageToolSettings>(StringComparer.Ordinal)
            {
                [toolId] = new(
                    "external_action",
                    null,
                    null,
                    null,
                    ActionCategory.Notification.ToStorageValue(),
                    "test:target")
            },
            [],
            new IngestionSettings("local", ["tester"]),
            new FaultGroupingSettings(15, 30, 1, new MassIssueSettings(5, "strong")),
            RedactionSettings.Default)
        {
            Actions = new TriageActionSettings(
                allowed ? [toolId] : [],
                mode.ToStorageValue(),
                requireApproval,
                60)
            {
                NotificationRoutes = allowed
                    ? [new NotificationRoute("test-route", toolId, null, null, ["error", "critical", "fatal"])]
                    : []
            }
        };
}
