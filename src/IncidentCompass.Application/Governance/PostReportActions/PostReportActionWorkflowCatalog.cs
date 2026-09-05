using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed class PostReportActionWorkflowCatalog
{
    private readonly Dictionary<(string ToolId, int Version), IPostReportActionWorkflow> workflows;

    public PostReportActionWorkflowCatalog(
        IEnumerable<IPostReportActionWorkflow> registeredWorkflows,
        IAgentToolRegistry toolRegistry)
    {
        var materialized = registeredWorkflows
            .OrderBy(static workflow => workflow.ToolId, StringComparer.Ordinal)
            .ThenBy(static workflow => workflow.WorkflowVersion)
            .ToArray();
        foreach (var workflow in materialized)
        {
            Validate(workflow, toolRegistry);
        }

        var duplicate = materialized
            .GroupBy(static workflow => (workflow.ToolId, workflow.WorkflowVersion))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Post-report workflow '{duplicate.Key.ToolId}' version {duplicate.Key.WorkflowVersion} is registered more than once.");
        }

        Workflows = materialized;
        workflows = materialized.ToDictionary(
            static workflow => (workflow.ToolId, workflow.WorkflowVersion));
    }

    public IReadOnlyList<IPostReportActionWorkflow> Workflows { get; }

    public bool TryGet(string toolId, int workflowVersion, out IPostReportActionWorkflow workflow) =>
        workflows.TryGetValue((toolId, workflowVersion), out workflow!);

    private static void Validate(
        IPostReportActionWorkflow workflow,
        IAgentToolRegistry toolRegistry)
    {
        if (!AgentToolIdentity.IsValid(workflow.ToolId) || workflow.WorkflowVersion != 1 ||
            string.IsNullOrWhiteSpace(workflow.LogicalTargetId) || workflow.LogicalTargetId.Length > 128 ||
            !toolRegistry.TryGet(workflow.ToolId, out var descriptor) ||
            descriptor.Capability != AgentToolCapability.ExternalAction ||
            descriptor.Category != workflow.Category ||
            !string.Equals(descriptor.LogicalTargetId, workflow.LogicalTargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Post-report workflow '{workflow.ToolId}' version {workflow.WorkflowVersion} does not match its backend descriptor.");
        }
    }
}
