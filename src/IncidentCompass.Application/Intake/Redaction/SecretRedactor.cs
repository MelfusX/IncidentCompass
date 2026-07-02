using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.Application.Intake.Redaction;

internal static partial class SecretRedactor
{
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
        signal with
        {
            ErrorMessage = RedactText(signal.ErrorMessage),
            Description = RedactText(signal.Description),
            Summary = RedactText(signal.Summary) ?? string.Empty,
            Attributes = RedactJsonNode(signal.Attributes),
            Body = RedactJsonNode(signal.Body),
        };

    public static string? RedactText(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var redacted = BearerTokenPattern().Replace(text, "Bearer [REDACTED]");
        redacted = AwsAccessKeyPattern().Replace(redacted, "[REDACTED]");
        redacted = SecretPrefixedTokenPattern().Replace(redacted, "[REDACTED]");
        redacted = ConnectionStringPasswordPattern().Replace(redacted, "$1=[REDACTED]");
        return redacted;
    }

    public static JsonNode RedactJsonNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => RedactObject(jsonObject),
            JsonArray jsonArray => RedactArray(jsonArray),
            JsonValue jsonValue => RedactValue(jsonValue),
            _ => node,
        };
    }

    private static JsonObject RedactObject(JsonObject jsonObject)
    {
        var result = new JsonObject();
        foreach (var property in jsonObject)
        {
            if (SecretPropertyNames.Contains(property.Key))
            {
                result[property.Key] = "[REDACTED]";
                continue;
            }

            result[property.Key] = property.Value is null ? null : RedactJsonNode(property.Value);
        }

        return result;
    }

    private static JsonArray RedactArray(JsonArray jsonArray)
    {
        var result = new JsonArray();
        foreach (var element in jsonArray)
        {
            result.Add(element is null ? null : RedactJsonNode(element));
        }

        return result;
    }

    private static JsonNode RedactValue(JsonValue jsonValue)
    {
        if (jsonValue.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return jsonValue.DeepClone();
        }

        var text = jsonValue.GetValue<string>();
        return JsonValue.Create(RedactText(text)!)!;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_\.=]{10,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyPattern();

    [GeneratedRegex(@"\b(?:sk|ghp|gho|ghu|ghs|glpat|xox[baprs])-[A-Za-z0-9\-_]{10,}\b")]
    private static partial Regex SecretPrefixedTokenPattern();

    [GeneratedRegex(@"(password|pwd)\s*=\s*[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordPattern();
}
