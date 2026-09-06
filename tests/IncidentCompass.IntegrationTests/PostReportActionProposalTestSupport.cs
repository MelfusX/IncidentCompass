using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.ActionApprovals.Testing;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

internal static class PostReportActionProposalTestSupport
{
    public static ServiceProvider Services(
        string connectionString,
        ProposalOnlyExternalActionTool tool,
        IActionApprovalTransactionFaultInjector? faultInjector = null) =>
        ActionApprovalTestSupport.CreateServices(
            connectionString,
            faultInjector,
            configureServices: services =>
            {
                services.AddSingleton(new AgentToolDescriptor(
                    tool.Definition.Name, AgentToolCapability.ExternalAction,
                    tool.Category, tool.LogicalTargetId));
                services.AddSingleton<IExternalActionTool>(tool);
            });

    public static Task<PostReportActionProposalResponse> ProposeAsync(
        ServiceProvider services,
        ActionApprovalOriginFixture origin,
        string proposalKey,
        JsonElement arguments,
        string toolId = "action_test") =>
        DispatchAsync(services, new ProposePostReportActionCommand(
            origin.TenantId, origin.ReportId, toolId, proposalKey, arguments));

    public static async Task<PostReportActionProposalResponse> DispatchAsync(
        ServiceProvider services,
        ProposePostReportActionCommand command)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>()
            .DispatchAsync<ProposePostReportActionCommand, PostReportActionProposalResponse>(
                command, TestContext.Current.CancellationToken);
    }

    public static async Task<T> StartAfterAsync<T>(
        Task start,
        Func<Task<T>> action)
    {
        await start;
        return await action();
    }

    public static async Task<ActionApprovalOriginFixture> PublishSuccessorAsync(
        ServiceProvider services,
        string connectionString,
        ActionApprovalOriginFixture origin)
    {
        var successor = await ActionApprovalTestSupport.SeedSuccessorJobAsync(
            connectionString, origin, "notification-successor-publisher");
        Guid reportId;
        using (var scope = services.CreateScope())
        {
            reportId = await scope.ServiceProvider.GetRequiredService<ITriageReportRepository>().PublishAsync(
                successor.Job, "notification-successor-publisher", successor.Report,
                TestContext.Current.CancellationToken);
        }

        return origin with { JobId = successor.Job.Id, ReportId = reportId };
    }

    public static JsonElement Arguments(string message) =>
        JsonSerializer.SerializeToElement(new { message });

    public static string Configuration(
        ActionCategory category,
        bool allowed = true,
        int rateCap = 10,
        string rateScope = "attempt",
        bool precondition = false,
        string globalMode = "live",
        string toolId = "action_test")
    {
        var rules = new JsonArray(new JsonObject
        {
            ["Type"] = "rate_cap",
            ["Tool"] = toolId,
            ["Scope"] = rateScope,
            ["Max"] = rateCap
        });
        if (precondition)
        {
            rules.Add(new JsonObject
            {
                ["Type"] = "precondition",
                ["Tool"] = toolId,
                ["Scope"] = "attempt",
                ["RequiresSuccessfulToolResult"] = toolId
            });
        }

        var root = new JsonObject
        {
            ["Providers"] = new JsonObject { ["mock"] = new JsonObject { ["Kind"] = "Mock" } },
            ["Routes"] = new JsonObject
            {
                ["chat"] = new JsonObject
                {
                    ["Kind"] = "Chat",
                    ["ProviderId"] = "mock",
                    ["Model"] = "mock-chat"
                }
            },
            ["Orchestrator"] = new JsonObject
            {
                ["Instructions"] = "orchestrator",
                ["RouteId"] = "chat",
                ["Tools"] = new JsonArray("delegate", "publish_report"),
                ["Budget"] = new JsonObject
                {
                    ["MaxWorkers"] = 1,
                    ["MaxTokens"] = 1000,
                    ["MaxWallClockSeconds"] = 30,
                    ["MaxReprompts"] = 0
                }
            },
            ["Roles"] = new JsonObject
            {
                ["analysis"] = new JsonObject
                {
                    ["RouteId"] = "chat",
                    ["Instructions"] = "analysis",
                    ["Tools"] = new JsonArray(),
                    ["OutputSchema"] = "{}"
                }
            },
            ["Tools"] = new JsonObject
            {
                [toolId] = new JsonObject
                {
                    ["Kind"] = "external_action",
                    ["Category"] = category.ToStorageValue(),
                    ["LogicalTargetId"] = "test:target"
                }
            },
            ["Rules"] = rules,
            ["Actions"] = new JsonObject
            {
                ["AllowedTools"] = allowed ? new JsonArray(toolId) : new JsonArray(),
                ["DefaultMode"] = globalMode,
                ["RequireApprovalForAll"] = false,
                ["ApprovalTtlMinutes"] = 60,
                ["NotificationRoutes"] = category == ActionCategory.Notification && allowed
                    ? new JsonArray(new JsonObject
                    {
                        ["RouteId"] = "test-route",
                        ["ToolId"] = toolId,
                        ["Severities"] = new JsonArray("error", "critical", "fatal")
                    })
                    : new JsonArray()
            },
            ["Ingestion"] = new JsonObject
            {
                ["DefaultTenant"] = "local",
                ["AllowedSources"] = new JsonArray("tester")
            },
            ["FaultGrouping"] = new JsonObject
            {
                ["LookbackMinutes"] = 15,
                ["SilenceWindowMinutes"] = 30,
                ["FingerprintVersion"] = 1,
                ["MassIssue"] = new JsonObject
                {
                    ["MinNeighborCount"] = 5,
                    ["MinFingerprintStrength"] = "strong"
                }
            },
            ["Redaction"] = new JsonObject
            {
                ["AttributeKeys"] = new JsonArray(),
                ["Patterns"] = new JsonArray(),
                ["UserIdentifierAttributes"] = new JsonArray()
            }
        };
        return root.ToJsonString();
    }

    public static Task<long> ActionCountAsync(string connectionString, Guid reportId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.action_approvals WHERE origin_report_id = @report;",
            ("report", reportId));

    public static Task<long> DenialCountAsync(string connectionString, Guid reportId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            """
            SELECT count(*) FROM incidentcompass.triage_ledger
            WHERE event_type = 'PolicyDecision' AND decision = 'Denied'
              AND payload_ref = @ref;
            """, ("ref", "report:" + reportId));

    public static Task<long> DenialToolCountAsync(string connectionString, string toolId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            """
            SELECT count(*) FROM incidentcompass.triage_ledger
            WHERE event_type = 'PolicyDecision' AND decision = 'Denied'
              AND tool_name = @tool_id;
            """, ("tool_id", toolId));

    public static async Task WaitForFaultLockWaiterAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var waiters = await ActionApprovalTestSupport.CountAsync(
                connectionString,
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM incidentcompass.faults%';
                """);
            if (waiters > 0)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The governed proposal did not reach the origin fault lock.");
    }

    public static Task<long> LedgerCountAsync(string connectionString, Guid jobId) =>
        ActionApprovalTestSupport.CountAsync(
            connectionString,
            "SELECT count(*) FROM incidentcompass.triage_ledger WHERE job_id = @job;",
            ("job", jobId));
}
