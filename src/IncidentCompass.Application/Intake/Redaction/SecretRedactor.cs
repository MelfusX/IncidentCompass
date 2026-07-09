using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.Application.Intake.Redaction;

internal static partial class SecretRedactor
{
    private static readonly TimeSpan ConfiguredPatternTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "accesskey",
        "access_key",
        "clientsecret",
        "client_secret",
        "connectionstring",
        "connection_string",
        "privatekey",
        "private_key",
        "authorization",
    };

    public static NormalizedSignal Redact(NormalizedSignal signal) =>
        Redact(signal, RedactionSettings.Default);

    public static NormalizedSignal Redact(NormalizedSignal signal, RedactionSettings settings) =>
        signal with
        {
            ExternalId = RedactText(signal.ExternalId, settings),
            TraceId = RedactText(signal.TraceId, settings),
            SpanId = RedactText(signal.SpanId, settings),
            ParentSpanId = RedactText(signal.ParentSpanId, settings),
            ServiceName = RedactRequiredText(signal.ServiceName, settings),
            Environment = RedactRequiredText(signal.Environment, settings),
            OperationName = RedactText(signal.OperationName, settings),
            Severity = RedactText(signal.Severity, settings),
            ErrorType = RedactText(signal.ErrorType, settings),
            ErrorMessage = RedactText(signal.ErrorMessage, settings),
            Description = SignalTextTruncator.TruncateDescription(RedactText(signal.Description, settings)),
            Summary = SignalTextTruncator.TruncateSummary(RedactRequiredText(signal.Summary, settings)),
            HttpMethod = RedactText(signal.HttpMethod, settings),
            HttpRoute = RedactText(signal.HttpRoute, settings),
            Attributes = RedactJsonNode(signal.Attributes, settings),
            Body = RedactJsonNode(signal.Body, settings),
        };

    private static string RedactRequiredText(string text, RedactionSettings settings) =>
        RedactText(text, settings) ?? string.Empty;

    public static string? RedactText(string? text) => RedactText(text, RedactionSettings.Default);

    public static string? RedactText(string? text, RedactionSettings settings)
    {
        if (text is null)
        {
            return null;
        }

        var redacted = BearerTokenPattern().Replace(text, "Bearer [REDACTED]");
        redacted = AwsAccessKeyPattern().Replace(redacted, "[REDACTED]");
        redacted = SecretPrefixedTokenPattern().Replace(redacted, "[REDACTED]");
        redacted = ConnectionStringPasswordPattern().Replace(redacted, "$1=[REDACTED]");
        foreach (var pattern in settings.Patterns)
        {
            var options = pattern.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            redacted = new Regex(pattern.Pattern, options, ConfiguredPatternTimeout)
                .Replace(redacted, pattern.Replacement);
        }

        return redacted;
    }

    public static JsonNode RedactJsonNode(JsonNode node) =>
        RedactJsonNode(node, RedactionSettings.Default);

    public static JsonNode RedactJsonNode(JsonNode node, RedactionSettings settings) =>
        RedactNode(node, settings, string.Empty);

    private static JsonNode RedactNode(JsonNode node, RedactionSettings settings, string path)
    {
        return node switch
        {
            JsonObject jsonObject => RedactObject(jsonObject, settings, path),
            JsonArray jsonArray => RedactArray(jsonArray, settings, path),
            JsonValue jsonValue => RedactValue(jsonValue, settings),
            _ => node.DeepClone(),
        };
    }

    private static JsonObject RedactObject(JsonObject jsonObject, RedactionSettings settings, string path)
    {
        var result = new JsonObject();
        foreach (var property in jsonObject)
        {
            var propertyPath = string.IsNullOrEmpty(path) ? property.Key : path + "." + property.Key;
            if (IsSensitiveProperty(property.Key, propertyPath, settings))
            {
                result[property.Key] = IsPseudonym(property.Value)
                    ? property.Value!.DeepClone()
                    : "[REDACTED]";
                continue;
            }

            result[property.Key] = property.Value is null
                ? null
                : RedactNode(property.Value, settings, propertyPath);
        }

        return result;
    }

    private static JsonArray RedactArray(JsonArray jsonArray, RedactionSettings settings, string path)
    {
        var result = new JsonArray();
        foreach (var element in jsonArray)
        {
            result.Add(element is null ? null : RedactNode(element, settings, path));
        }

        return result;
    }

    private static JsonNode RedactValue(JsonValue jsonValue, RedactionSettings settings)
    {
        if (jsonValue.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return jsonValue.DeepClone();
        }

        var text = jsonValue.GetValue<string>();
        return JsonValue.Create(RedactText(text, settings)!)!;
    }

    private static bool IsSensitiveProperty(string key, string path, RedactionSettings settings) =>
        SecretPropertyNames.Contains(key) ||
        settings.AttributeKeys.Any(candidate =>
            string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));

    private static bool IsPseudonym(JsonNode? value) =>
        value is JsonValue jsonValue &&
        jsonValue.GetValueKind() == System.Text.Json.JsonValueKind.String &&
        jsonValue.GetValue<string>().StartsWith(UserIdentifierPseudonymizer.Prefix, StringComparison.Ordinal);

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_\.=]{10,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyPattern();

    [GeneratedRegex(@"\b(?:(?:sk|glpat|xox[baprs])-[A-Za-z0-9\-_]{10,}|(?:ghp|gho|ghu|ghs)[_-][A-Za-z0-9\-_]{10,}|github_pat_[A-Za-z0-9_]{10,})\b")]
    private static partial Regex SecretPrefixedTokenPattern();

    [GeneratedRegex(@"(password|pwd)\s*=\s*[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordPattern();
}
