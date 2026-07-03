using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Governance.Tools;

public sealed record AgentToolExecutionContext(
    TriageJob Job,
    TriageConfiguration Configuration,
    string RoleName,
    string ToolName,
    string TenantId);
