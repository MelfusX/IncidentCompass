using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.Tools;

public sealed record AgentToolDescriptor(
    string ToolId,
    AgentToolCapability Capability,
    ActionCategory? Category = null,
    string? LogicalTargetId = null);
