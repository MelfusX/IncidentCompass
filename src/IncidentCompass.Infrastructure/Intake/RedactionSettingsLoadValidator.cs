using System.Text.RegularExpressions;
using IncidentCompass.Application.Intake.Configuration;
using static IncidentCompass.Infrastructure.Intake.TriageConfigurationValidationGuards;

namespace IncidentCompass.Infrastructure.Intake;

internal static class RedactionSettingsLoadValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    public static void Validate(RedactionSettings settings)
    {
        ValidateKeys(settings.AttributeKeys, "Redaction.AttributeKeys");
        ValidateKeys(settings.UserIdentifierAttributes, "Redaction.UserIdentifierAttributes");

        for (var index = 0; index < settings.Patterns.Count; index++)
        {
            var pattern = settings.Patterns.ElementAt(index);
            var section = $"Redaction.Patterns[{index}]";
            RequireNonBlank(section + ".Name", pattern.Name);
            RequireNonBlank(section + ".Pattern", pattern.Pattern);
            RequireNonBlank(section + ".Replacement", pattern.Replacement);
            try
            {
                _ = new Regex(pattern.Pattern, RegexOptions.None, PatternTimeout);
            }
            catch (ArgumentException exception)
            {
                throw Invalid(section + ".Pattern", pattern.Pattern, "a valid .NET regular expression: " + exception.Message);
            }
        }
    }

    private static void ValidateKeys(IReadOnlyCollection<string> keys, string section)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            RequireNonBlank(section, key);
            if (!distinct.Add(key))
            {
                throw Invalid(section, key, "unique attribute keys");
            }
        }
    }
}
