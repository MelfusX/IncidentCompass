using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Governance;
using System.Text.Json;

namespace IncidentCompass.Application.Governance.Tools.Execution;

internal sealed record AgentToolExecutionResult(
    string ToolCallId,
    string ToolName,
    string ResponseSchemaVersion,
    string AuditSchemaVersion,
    ToolValidationResult Validation,
    ToolPolicyDecision Policy,
    ToolApprovalState ApprovalState,
    ToolExecutionStatus ExecutionStatus,
    JsonElement? Output,
    string? ErrorCode,
    string? ErrorMessage,
    Exception? Exception = null)
{
    public string? ResultText => Output?.GetRawText();
}
