using System.Text.Json;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Application.Notifications;

public sealed class TelegramNotificationWorkflow(
    ITriageConfigurationRepository configurationRepository,
    IServiceScopeFactory scopeFactory) : IPostReportActionWorkflow
{
    public const string ToolIdValue = TelegramNotificationToolDescriptor.ToolId;
    public const string LogicalTargetIdValue = TelegramNotificationToolDescriptor.LogicalTargetId;

    public string ToolId => ToolIdValue;

    public int WorkflowVersion => 1;

    public ActionCategory Category => ActionCategory.Notification;

    public string LogicalTargetId => LogicalTargetIdValue;

    public async Task<(bool ShouldEnqueue, string? RouteId)> SelectAsync(
        string tenantId,
        Guid originReportId,
        Guid faultId,
        Guid jobId,
        int attempt,
        string configHash,
        string serviceName,
        string environment,
        string? severity,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetByHashAsync(configHash, cancellationToken);
        var selected = NotificationRouteSelector.Select(
            configuration.Actions.NotificationRoutes, serviceName, environment, severity);
        return selected is not null &&
               string.Equals(selected.ToolId, ToolId, StringComparison.Ordinal) &&
               IsEnabled(configuration)
            ? (true, selected.RouteId)
            : (false, null);
    }

    public async Task<PostReportActionWorkflowResult> EvaluateAsync(
        PostReportActionIntent intent,
        CancellationToken cancellationToken)
    {
        if (!HasMatchingIdentity(intent))
        {
            return PostReportActionWorkflowResult.DeadLetter("notification_intent_invalid");
        }

        var configuration = await configurationRepository.GetByHashAsync(intent.ConfigHash, cancellationToken);
        if (!IsEnabled(configuration) ||
            !configuration.Actions.NotificationRoutes.Any(route =>
                string.Equals(route.RouteId, intent.RouteId, StringComparison.Ordinal) &&
                string.Equals(route.ToolId, ToolId, StringComparison.Ordinal)))
        {
            return PostReportActionWorkflowResult.Completed("notification_route_unavailable");
        }

        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>();
        var response = await dispatcher.DispatchAsync<
            ProposePostReportActionCommand,
            PostReportActionProposalResponse>(
            new ProposePostReportActionCommand(
                intent.TenantId,
                intent.OriginReportId,
                ToolId,
                intent.ProposalKey,
                JsonSerializer.SerializeToElement(new
                {
                    originReportId = intent.OriginReportId.ToString("N"),
                    routeId = intent.RouteId
                })),
            cancellationToken);
        return PostReportActionWorkflowResult.Completed(response.Outcome switch
        {
            PostReportActionProposalOutcome.Approved => "approved",
            PostReportActionProposalOutcome.Requested => "requested",
            _ => response.ReasonCode
        });
    }

    private bool HasMatchingIdentity(PostReportActionIntent intent) =>
        string.Equals(intent.ToolId, ToolId, StringComparison.Ordinal) &&
        intent.WorkflowVersion == WorkflowVersion &&
        intent.RouteId is not null &&
        intent.HasValidCanonicalInput();

    private bool IsEnabled(TriageConfiguration configuration) =>
        configuration.Actions.AllowedTools.Contains(ToolId, StringComparer.Ordinal) &&
        configuration.Tools.TryGetValue(ToolId, out var tool) &&
        string.Equals(tool.Kind, "external_action", StringComparison.Ordinal) &&
        string.Equals(tool.Category, ActionCategory.Notification.ToStorageValue(), StringComparison.Ordinal) &&
        string.Equals(tool.LogicalTargetId, LogicalTargetId, StringComparison.Ordinal) &&
        !string.Equals(configuration.Actions.DefaultMode, "disabled", StringComparison.Ordinal) &&
        !string.Equals(tool.Mode, "disabled", StringComparison.Ordinal);
}
