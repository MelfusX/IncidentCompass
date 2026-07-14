using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IncidentCompass.Infrastructure.Intake;

internal static partial class EnvironmentPlaceholderExpander
{
    public static void Expand(JsonNode node)
    {
        ExpandNode(node);
    }

    private static void ExpandNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var key in jsonObject.Select(static property => property.Key).ToArray())
                {
                    var child = jsonObject[key];
                    if (TryExpandString(child, out var expanded))
                    {
                        jsonObject[key] = JsonValue.Create(expanded);
                        continue;
                    }

                    ExpandNode(child);
                }

                break;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var child = jsonArray[index];
                    if (TryExpandString(child, out var expanded))
                    {
                        jsonArray[index] = JsonValue.Create(expanded);
                        continue;
                    }

                    ExpandNode(child);
                }

                break;
        }
    }

    private static bool TryExpandString(JsonNode? node, out string expanded)
    {
        expanded = string.Empty;
        if (node is not JsonValue jsonValue || jsonValue.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return false;
        }

        expanded = ExpandString(jsonValue.GetValue<string>());
        return true;
    }

    private static string ExpandString(string value)
    {
        return PlaceholderRegex().Replace(value, static match =>
        {
            var name = match.Groups["name"].Value;
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            if (match.Groups["fallback"].Success)
            {
                return match.Groups["fallback"].Value;
            }

            throw new InvalidOperationException("Environment variable '" + name + "' is required by triage configuration.");
        });
    }

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(:-(?<fallback>[^}]*))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
