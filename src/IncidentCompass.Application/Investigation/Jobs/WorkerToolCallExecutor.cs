using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Observability;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Governance;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerToolCallExecutor(
    IEnumerable<IImmediateAgentTool> tools,
    ToolRuleEngine ruleEngine,
    TriageLedgerAppender ledgerAppender,
    ITriageToolResultCommitter toolResultCommitter,
    IRuntimeTelemetry? telemetry = null)
{
    private readonly IReadOnlyList<IImmediateAgentTool> tools = tools.ToArray();

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
        TriageJobInvestigationContext investigationContext,
        string roleName,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        using var toolTelemetry = telemetry?.StartToolCall();
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
        var validation = tool?.Validate(toolCall.Arguments);
        var decision = await DecideAsync(job, configuration, roleName, toolCall, tool, validation, cancellationToken);
        await ledgerAppender.AppendPolicyDecisionAsync(
            job,
            roleName,
            toolCall.Name,
            decision.Decision,
            decision.Reason,
            cancellationToken);

        if (decision.Decision == TriageLedgerDecision.ApprovalRequired)
        {
            telemetry?.RecordToolCall(RuntimeTelemetryOutcome.Denied);
            return SerializeToolFailure(ToolExecutionStatus.ApprovalRequired.ToString(), "approval_required", decision.Reason, limitation: decision.Reason);
        }

        if (!decision.MayProceed)
        {
            telemetry?.RecordToolCall(RuntimeTelemetryOutcome.Denied);
            throw new InvalidOperationException("Worker tool call denied: " + decision.Reason);
        }

        if (validation is null || !validation.IsValid)
        {
            telemetry?.RecordToolCall(RuntimeTelemetryOutcome.Denied);
            throw new InvalidOperationException("Worker tool call validation failed after policy approval.");
        }

        var execution = await tool!.ExecuteAsync(
            new AgentToolExecutionContext(
                job,
                configuration,
                roleName,
                toolCall.Name,
                investigationContext.Fault.TenantId,
                investigationContext.Fault.ServiceName)
            {
                TriggerSignal = investigationContext.TriggerSignal,
                FaultFingerprint = investigationContext.Fault.Fingerprint
            },
            validation.SanitizedArguments,
            cancellationToken);
        if (execution.Status == ToolExecutionStatus.Succeeded)
        {
            await CommitSucceededAsync(job, roleName, toolCall.Name, execution.Output, execution.Artifacts, cancellationToken);
            telemetry?.RecordToolCall(RuntimeTelemetryOutcome.Succeeded);
            return execution.Output.GetRawText();
        }

        var errorReason = execution.ErrorMessage ?? "Tool execution failed.";
        await AppendExecutedToolFailureAsync(job, roleName, toolCall.Name, execution.Status.ToString(), errorReason, cancellationToken);
        telemetry?.RecordToolCall(RuntimeTelemetryOutcome.Failed);
        return SerializeToolFailure(execution.Status.ToString(), execution.ErrorCode, errorReason, limitation: errorReason);
    }

    private async Task<ToolRulePolicyResult> DecideAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        AiToolCall toolCall,
        IImmediateAgentTool? tool,
        ToolValidationResult? validation,
        CancellationToken cancellationToken)
    {
        if (tool is null)
        {
            return ToolRulePolicyResult.Denied("tool_not_registered");
        }

        if (validation is null || !validation.IsValid)
        {
            return ToolRulePolicyResult.Denied(validation?.ErrorMessage ?? "tool_arguments_invalid");
        }

        return await ruleEngine.DecideImmediateAsync(job, configuration, roleName, toolCall.Name, cancellationToken);
    }

    private async Task CommitSucceededAsync(
        TriageJob job,
        string roleName,
        string toolName,
        JsonElement output,
        IReadOnlyCollection<TriageArtifact>? artifacts,
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
                "Tool completed successfully.",
                artifacts),
            cancellationToken);
    }

    private async Task AppendExecutedToolFailureAsync(
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
