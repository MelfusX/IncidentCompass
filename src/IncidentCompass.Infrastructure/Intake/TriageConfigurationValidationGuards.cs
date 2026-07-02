using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Infrastructure.Intake;

internal static class TriageConfigurationValidationGuards
{
    public static void RequireChatRoute(IReadOnlyDictionary<string, TriageRouteSettings> routes, string routeId, string settingName)
    {
        RequireRouteKind(routes, routeId, settingName, "Chat");
    }

    public static void RequireEmbeddingRoute(IReadOnlyDictionary<string, TriageRouteSettings> routes, string routeId, string settingName)
    {
        RequireRouteKind(routes, routeId, settingName, "Embedding");
    }

    public static void RequireKnown(string settingName, string value, IReadOnlyCollection<string> allowedValues)
    {
        RequireNonBlank(settingName, value);
        if (!allowedValues.Contains(value))
        {
            throw Invalid(settingName, value, "one of: " + string.Join(", ", allowedValues));
        }
    }

    public static void RequireKey(string key, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw Invalid(sectionName, key, "non-blank keys");
        }
    }

    public static void RequireNonBlank(string settingName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(settingName, value, "a non-blank value");
        }
    }

    public static TriageConfigurationLoadException Invalid(string name, string value, string expected)
    {
        return TriageConfigurationLoadException.InvalidSetting(name, value, expected);
    }

    private static void RequireRouteKind(
        IReadOnlyDictionary<string, TriageRouteSettings> routes,
        string routeId,
        string settingName,
        string expectedKind)
    {
        if (!routes.TryGetValue(routeId, out var route))
        {
            throw Invalid(settingName, routeId, "a configured " + expectedKind + " route id");
        }

        if (!string.Equals(route.Kind, expectedKind, StringComparison.Ordinal))
        {
            throw Invalid(settingName, routeId, "a configured " + expectedKind + " route id");
        }
    }
}
