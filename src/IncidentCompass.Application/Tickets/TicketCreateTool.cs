using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Tickets;

public static class TicketCreateTool
{
    public const string ToolId = "ticket_create";

    public const string LogicalTargetId = "ticket:configured-repository";

    public static AgentToolDescriptor Descriptor { get; } = new(
        ToolId,
        AgentToolCapability.ExternalAction,
        ActionCategory.TicketCreate,
        LogicalTargetId);
}
