using System.Text.Json;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Propose;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed class TicketUpdatePostReportActionWorkflow(
    ITriageConfigurationRepository configurationRepository,
    IServiceScopeFactory scopeFactory) : IPostReportActionWorkflow
{
    public const string UpdateToolId = "ticket_update";
    public const string UpdateLogicalTargetId = "ticket:configured-repository";

    public static AgentToolDescriptor Descriptor { get; } = new(
        UpdateToolId,
        AgentToolCapability.ExternalAction,
        ActionCategory.TicketUpdate,
        UpdateLogicalTargetId);

    public string ToolId => UpdateToolId;
    public int WorkflowVersion => 1;
    public ActionCategory Category => ActionCategory.TicketUpdate;
    public string LogicalTargetId => UpdateLogicalTargetId;

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
            return PostReportActionWorkflowResult.DeadLetter("ticket_update_intent_invalid");
        }

        var configuration = await configurationRepository.GetByHashAsync(intent.ConfigHash, cancellationToken);
        if (!IsEnabled(configuration))
        {
            return PostReportActionWorkflowResult.Completed("ticket_update_disabled");
        }

        using var scope = scopeFactory.CreateScope();
        var evidence = await scope.ServiceProvider.GetRequiredService<ITicketUpdateEvidenceResolver>()
            .ResolveAsync(intent.TenantId, intent.OriginReportId, cancellationToken);
        if (evidence is null)
        {
            return PostReportActionWorkflowResult.Completed("ticket_update_target_required");
        }

        var response = await scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>()
            .DispatchAsync<ProposePostReportActionCommand, PostReportActionProposalResponse>(
                new ProposePostReportActionCommand(
                    intent.TenantId,
                    intent.OriginReportId,
                    ToolId,
                    intent.ProposalKey,
                    JsonSerializer.SerializeToElement(new
                    {
                        originReportId = intent.OriginReportId.ToString("N"),
                        proposalKey = intent.ProposalKey,
                        ticketId = evidence.TicketId
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
