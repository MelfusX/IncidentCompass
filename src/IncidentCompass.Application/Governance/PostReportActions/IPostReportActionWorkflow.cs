using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.PostReportActions;

public interface IPostReportActionWorkflow
{
    string ToolId { get; }

    int WorkflowVersion { get; }

    ActionCategory Category { get; }

    string LogicalTargetId { get; }

    Task<(bool ShouldEnqueue, string? RouteId)> SelectAsync(
        string tenantId,
        Guid originReportId,
        Guid faultId,
        Guid jobId,
        int attempt,
        string configHash,
        string serviceName,
        string environment,
        string? severity,
        CancellationToken cancellationToken);

    Task<PostReportActionWorkflowResult> EvaluateAsync(
        PostReportActionIntent intent,
        CancellationToken cancellationToken);
}
