namespace IncidentCompass.Application.Notifications;

public sealed record NotificationRoute(
    string RouteId,
    string ToolId,
    string? ServiceName,
    string? Environment,
    IReadOnlyCollection<string> Severities);
