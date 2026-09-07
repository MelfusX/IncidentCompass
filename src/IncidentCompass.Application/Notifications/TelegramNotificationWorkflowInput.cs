namespace IncidentCompass.Application.Notifications;

public sealed record TelegramNotificationWorkflowInput(
    Guid OriginReportId,
    string RouteId);
