using System.Text.Json.Serialization;

namespace IncidentCompass.Domain.Governance;

[JsonConverter(typeof(JsonStringEnumConverter<ToolExecutionStatus>))]
public enum ToolExecutionStatus
{
    NotExecuted,
    Rejected,
    ValidationFailed,
    ApprovalRequired,
    Failed,
    Succeeded
}
