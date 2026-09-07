using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Application.Intake.Normalization;

public static class OtelTriggerPolicy
{
    public static bool ShouldTrigger(IngestSignalCommand command, OtelTriggerSettings settings)
    {
        if (!Matches(settings.ServiceAllowList, command.ServiceName) ||
            !Matches(settings.SeverityAllowList, command.Severity))
        {
            return false;
        }

        return !settings.ErrorsOnly || IsError(command);
    }

    private static bool IsError(IngestSignalCommand command)
    {
        if (command.Severity is not null &&
            (command.Severity.Equals("error", StringComparison.OrdinalIgnoreCase) ||
             command.Severity.Equals("fatal", StringComparison.OrdinalIgnoreCase) ||
             command.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return HasText(command.Attributes, "errorType") ||
               HasText(command.Attributes, "exception.type") ||
               HasText(command.Attributes, "error.type") ||
               GetInt(command.Attributes, "httpStatusCode") is >= 500 ||
               GetInt(command.Attributes, "http.response.status_code") is >= 500;
    }

    private static bool Matches(IReadOnlyCollection<string> allowedValues, string? candidate) =>
        allowedValues.Count == 0 ||
        (!string.IsNullOrWhiteSpace(candidate) && allowedValues.Contains(candidate, StringComparer.OrdinalIgnoreCase));

    private static bool HasText(JsonNode? attributes, string name) =>
        attributes is JsonObject objectNode &&
        objectNode[name] is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text);

    private static int? GetInt(JsonNode? attributes, string name)
    {
        if (attributes is not JsonObject objectNode || objectNode[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue
            ? (int)longValue
            : null;
    }
}
