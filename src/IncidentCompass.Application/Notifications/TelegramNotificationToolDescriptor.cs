using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Notifications;

public static class TelegramNotificationToolDescriptor
{
    public const string ToolId = "telegram_notify";

    public const string LogicalTargetId = "telegram:incident-alerts";

    public static AgentToolDescriptor Value { get; } = new(
        ToolId,
        AgentToolCapability.ExternalAction,
        ActionCategory.Notification,
        LogicalTargetId);
}
