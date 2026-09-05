using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Governance.Tools;

public sealed record AgentToolExecutionContext(
    TriageJob Job,
    TriageConfiguration Configuration,
    string RoleName,
    string ToolName,
    string TenantId,
    string FaultServiceName)
{
    public Guid ConversationId { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string PolicyVersion { get; init; } = string.Empty;

    public bool ApproveRiskyTools { get; init; }

    public Signal? TriggerSignal { get; init; }

    public string FaultFingerprint { get; init; } = string.Empty;
}
