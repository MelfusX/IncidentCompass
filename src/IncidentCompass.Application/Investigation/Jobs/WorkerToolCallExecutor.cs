using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Governance;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerToolCallExecutor(
    IEnumerable<IAgentTool> tools,
    WorkerToolRuleEngine ruleEngine,
    TriageLedgerAppender ledgerAppender,
    ITriageToolResultCommitter toolResultCommitter)
{
    private readonly IReadOnlyList<IAgentTool> tools = tools.ToArray();

    public IReadOnlyList<AiToolDefinition> CreateToolSurface(TriageConfiguration configuration, TriageRoleSettings role)
    {
        var granted = role.Tools.ToHashSet(StringComparer.Ordinal);
        return tools
            .Where(tool => granted.Contains(tool.Definition.Name) && configuration.Tools.ContainsKey(tool.Definition.Name))
            .Select(static tool => tool.Definition)
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> ExecuteAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        await ledgerAppender.AppendAsync(
            job,
            TriageLedgerEventType.ToolProposed,
            roleName,
            toolCall.Name,
            "Worker proposed tool call.",
            payloadRef: toolCall.Id,
            cancellationToken);

        var tool = tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Definition.Name, toolCall.Name, StringComparison.Ordinal));
        var decision = await DecideAsync(job, configuration, roleName, toolCall, tool, cancellationToken);
        await ledgerAppender.AppendPolicyDecisionAsync(
            job,
            roleName,
            toolCall.Name,
            decision.Decision,
            decision.Reason,
            cancellationToken);

        if (decision.Decision == TriageLedgerDecision.ApprovalRequired)
        {
            await AppendNonExecutedToolResultAsync(job, roleName, toolCall.Name, ToolExecutionStatus.ApprovalRequired.ToString(), decision.Reason, cancellationToken);
            return SerializeToolFailure(ToolExecutionStatus.ApprovalRequired.ToString(), "approval_required", decision.Reason, limitation: decision.Reason);
        }

        if (!decision.MayExecute)
        {
            await AppendNonExecutedToolResultAsync(job, roleName, toolCall.Name, ToolExecutionStatus.Rejected.ToString(), decision.Reason, cancellationToken);
            throw new InvalidOperationException("Worker tool call denied: " + decision.Reason);
        }

        var validation = tool!.Validate(toolCall.Arguments);
        if (!validation.IsValid)
        {
            var reason = validation.ErrorMessage ?? "Tool arguments failed validation.";
            await AppendNonExecutedToolResultAsync(job, roleName, toolCall.Name, ToolExecutionStatus.ValidationFailed.ToString(), reason, cancellationToken);
            throw new InvalidOperationException("Worker tool call validation failed: " + reason);
        }

        var execution = await tool.ExecuteAsync(validation.SanitizedArguments, cancellationToken);
        if (execution.Status == ToolExecutionStatus.Succeeded)
        {
            await CommitSucceededAsync(job, roleName, toolCall.Name, execution.Output, cancellationToken);
            return execution.Output.GetRawText();
        }

        var errorReason = execution.ErrorMessage ?? "Tool execution failed.";
        await AppendNonExecutedToolResultAsync(job, roleName, toolCall.Name, execution.Status.ToString(), errorReason, cancellationToken);
        return SerializeToolFailure(execution.Status.ToString(), execution.ErrorCode, errorReason, limitation: errorReason);
    }

    private async Task<WorkerToolPolicyResult> DecideAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        AiToolCall toolCall,
        IAgentTool? tool,
        CancellationToken cancellationToken)
    {
        if (tool is null)
        {
            return WorkerToolPolicyResult.Denied("tool_not_registered");
        }

        var validation = tool.Validate(toolCall.Arguments);
        if (!validation.IsValid)
        {
            return WorkerToolPolicyResult.Denied(validation.ErrorMessage ?? "tool_arguments_invalid");
        }

        return await ruleEngine.DecideAsync(job, configuration, roleName, toolCall.Name, cancellationToken);
    }

    private async Task CommitSucceededAsync(
        TriageJob job,
        string roleName,
        string toolName,
        JsonElement output,
        CancellationToken cancellationToken)
    {
        var canonicalPayload = CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(output.GetRawText())!);
        await toolResultCommitter.CommitSucceededAsync(
            new TriageToolResultCommitRequest(
                job,
                roleName,
                toolName,
                output,
                CanonicalJsonSerializer.ComputeSha256Hex(canonicalPayload),
                "Tool completed successfully."),
            cancellationToken);
    }

    private async Task AppendNonExecutedToolResultAsync(
        TriageJob job,
        string roleName,
        string toolName,
        string status,
        string reason,
        CancellationToken cancellationToken)
    {
        var rationale = status + ": " + reason;
        await ledgerAppender.AppendToolResultAsync(
            job,
            roleName,
            toolName,
            TriageLedgerToolStatus.Failed,
            rationale,
            payloadRef: null,
            cancellationToken);
    }

    private static string SerializeToolFailure(
        string status,
        string? errorCode,
        string errorMessage,
        string? limitation)
    {
        return JsonSerializer.Serialize(new
        {
            status,
            errorCode,
            errorMessage,
            limitation
        });
    }
}