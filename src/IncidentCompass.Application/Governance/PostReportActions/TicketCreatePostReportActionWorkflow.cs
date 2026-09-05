using System.Text.Json;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed class TicketCreatePostReportActionWorkflow(
    ITriageConfigurationRepository configurationRepository,
    IServiceScopeFactory scopeFactory) : IPostReportActionWorkflow
{
    public string ToolId => TicketCreateTool.ToolId;

    public int WorkflowVersion => 1;

    public ActionCategory Category => ActionCategory.TicketCreate;

    public string LogicalTargetId => TicketCreateTool.LogicalTargetId;

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
        return IsEnabled(configuration) ? (true, null) : (false, null);
    }

    public async Task<PostReportActionWorkflowResult> EvaluateAsync(
        PostReportActionIntent intent,
        CancellationToken cancellationToken)
    {
        if (!HasMatchingIdentity(intent))
        {
            return PostReportActionWorkflowResult.DeadLetter("ticket_create_intent_invalid");
        }

        var configuration = await configurationRepository.GetByHashAsync(intent.ConfigHash, cancellationToken);
        if (!IsEnabled(configuration))
        {
            return PostReportActionWorkflowResult.Completed("ticket_create_disabled");
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
                    proposalKey = intent.ProposalKey
                })),
            cancellationToken);
        return PostReportActionWorkflowResult.Completed(response.Outcome switch
        {
            PostReportActionProposalOutcome.Requested => "requested",
            PostReportActionProposalOutcome.Approved => "approved",
            _ => response.ReasonCode
        });
    }

    private bool HasMatchingIdentity(PostReportActionIntent intent) =>
        string.Equals(intent.ToolId, ToolId, StringComparison.Ordinal) &&
        intent.WorkflowVersion == WorkflowVersion && intent.RouteId is null &&
        intent.HasValidCanonicalInput();

    private bool IsEnabled(TriageConfiguration configuration) =>
        configuration.Actions.AllowedTools.Contains(ToolId, StringComparer.Ordinal) &&
        configuration.Tools.TryGetValue(ToolId, out var tool) &&
        string.Equals(tool.Kind, "external_action", StringComparison.Ordinal) &&
        string.Equals(tool.Category, Category.ToStorageValue(), StringComparison.Ordinal) &&
        string.Equals(tool.LogicalTargetId, LogicalTargetId, StringComparison.Ordinal) &&
        !string.Equals(configuration.Actions.DefaultMode, "disabled", StringComparison.Ordinal) &&
        !string.Equals(tool.Mode, "disabled", StringComparison.Ordinal);
}
