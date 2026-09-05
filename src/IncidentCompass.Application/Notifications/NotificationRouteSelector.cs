using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Notifications;

public static class NotificationRouteSelector
{
    private static readonly HashSet<string> Severities = new(
        ["error", "critical", "fatal"], StringComparer.Ordinal);

    public static NotificationRoute? Select(
        IReadOnlyList<NotificationRoute> routes,
        string serviceName,
        string environment,
        string? severity)
    {
        var normalizedService = Normalize(serviceName);
        var normalizedEnvironment = Normalize(environment);
        var normalizedSeverity = Normalize(severity);
        foreach (var route in routes)
        {
            if (Matches(route.ServiceName, normalizedService) &&
                Matches(route.Environment, normalizedEnvironment) &&
                route.Severities.Contains(normalizedSeverity, StringComparer.Ordinal))
            {
                return route;
            }
        }

        return null;
    }

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    public static bool TryValidateConfiguration(
        IReadOnlyDictionary<string, TriageToolSettings> tools,
        TriageActionSettings actions,
        out string field,
        out string value,
        out string expectation)
    {
        if (actions.NotificationRoutes is null)
        {
            return Fail("Actions.NotificationRoutes", "null", "an ordered route array",
                out field, out value, out expectation);
        }

        if (actions.NotificationRoutes.Count > 32)
        {
            return Fail("Actions.NotificationRoutes", actions.NotificationRoutes.Count.ToString(),
                "at most 32 ordered routes", out field, out value, out expectation);
        }

        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        var toolIds = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in actions.NotificationRoutes)
        {
            if (route is null)
            {
                return Fail("Actions.NotificationRoutes", "null", "a notification route object",
                    out field, out value, out expectation);
            }

            if (!AgentToolIdentity.IsValid(route.RouteId) || !routeIds.Add(route.RouteId))
            {
                return Fail("Actions.NotificationRoutes.RouteId", route.RouteId,
                    "a unique safe case-sensitive route id", out field, out value, out expectation);
            }

            if (!AgentToolIdentity.IsValid(route.ToolId) || !toolIds.Add(route.ToolId) ||
                !actions.AllowedTools.Contains(route.ToolId, StringComparer.Ordinal) ||
                !tools.TryGetValue(route.ToolId, out var tool) ||
                !string.Equals(tool.Kind, "external_action", StringComparison.Ordinal) ||
                !string.Equals(tool.Category, "notification", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(tool.LogicalTargetId))
            {
                return Fail("Actions.NotificationRoutes.ToolId", route.ToolId,
                    "one allowed registered notification tool", out field, out value, out expectation);
            }

            if (!targets.Add(tool.LogicalTargetId))
            {
                return Fail("Actions.NotificationRoutes.ToolId", route.ToolId,
                    "a unique notification logical target", out field, out value, out expectation);
            }

            if (!IsValidSelector(route.ServiceName))
            {
                return Fail("Actions.NotificationRoutes.ServiceName", route.ServiceName ?? "",
                    "a normalized safe selector", out field, out value, out expectation);
            }

            if (!IsValidSelector(route.Environment))
            {
                return Fail("Actions.NotificationRoutes.Environment", route.Environment ?? "",
                    "a normalized safe selector", out field, out value, out expectation);
            }

            if (route.Severities is null || route.Severities.Count == 0 ||
                route.Severities.Count > Severities.Count ||
                route.Severities.Any(severity => !Severities.Contains(severity)) ||
                route.Severities.Distinct(StringComparer.Ordinal).Count() != route.Severities.Count)
            {
                return Fail("Actions.NotificationRoutes.Severities",
                    route.Severities is null ? "null" : string.Join(",", route.Severities),
                    "a non-empty unique subset of error, critical, fatal",
                    out field, out value, out expectation);
            }
        }

        var routedTools = tools
            .Where(static pair => string.Equals(pair.Value.Category, "notification", StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .Where(toolId => actions.AllowedTools.Contains(toolId, StringComparer.Ordinal));
        if (routedTools.Any(toolId => !toolIds.Contains(toolId)))
        {
            return Fail("Actions.NotificationRoutes", "missing",
                "exactly one route for every allowed notification tool",
                out field, out value, out expectation);
        }

        field = value = expectation = string.Empty;
        return true;
    }

    private static bool Matches(string? selector, string normalizedValue) =>
        selector is null || string.Equals(selector, normalizedValue, StringComparison.Ordinal);

    private static bool IsValidSelector(string? value) =>
        value is null ||
        (value.Length is >= 1 and <= 128 &&
         string.Equals(value, Normalize(value), StringComparison.Ordinal) &&
         value.All(static character =>
             char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '/'));

    private static bool Fail(
        string errorField,
        string invalidValue,
        string expected,
        out string field,
        out string value,
        out string expectation)
    {
        field = errorField;
        value = invalidValue;
        expectation = expected;
        return false;
    }
}
