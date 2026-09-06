using System.Security.Cryptography;
using System.Text.Json;

namespace IncidentCompass.Tester;

internal static class DemoInjectionScenarioLoader
{
    internal const string ExpectedSha256 = "AF2C179FF50AB66B31EB44FBC68581B2CE74A2A94AF53E3564A322F2368130B0";
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Samples",
        "Incidents",
        "tester-ticket-action-injection.json");

    public static DemoScenario Load() => Load(FixturePath);

    internal static DemoScenario Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actualHash, ExpectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Tester injection fixture does not match the reviewed bytes.");
        }

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var correlation = root.GetProperty("correlation");
        var attributes = root.GetProperty("attributes");
        var observedAtUtc = root.GetProperty("observedAtUtc").GetDateTimeOffset();
        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("The Tester injection fixture timestamp must use UTC.");
        }

        var envelope = new IncidentEnvelope(
            RequiredString(root, "source"),
            RequiredString(root, "serviceName"),
            RequiredString(root, "environment"),
            RequiredString(root, "severity"),
            observedAtUtc,
            new IncidentCorrelation(
                RequiredString(correlation, "traceId"),
                RequiredString(correlation, "spanId"),
                RequiredString(correlation, "externalId")),
            new IncidentAttributes(
                RequiredString(attributes, "errorType"),
                RequiredString(attributes, "errorMessage"),
                RequiredString(attributes, "route"),
                RequiredString(attributes, "operation"),
                attributes.GetProperty("statusCode").GetInt32()),
            ReadMetadata(root.GetProperty("metadata")));

        return DemoScenario.CreateInjection(envelope);
    }

    private static Dictionary<string, object?> ReadMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The Tester injection fixture metadata must be an object.");
        }

        return metadata
            .EnumerateObject()
            .ToDictionary(
                static property => property.Name,
                static property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
    }

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        var value = parent.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("The Tester injection fixture contains an empty required field.")
            : value;
    }
}
