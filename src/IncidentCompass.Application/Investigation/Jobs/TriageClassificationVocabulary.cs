using System.Text.Json.Nodes;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class TriageClassificationVocabulary
{
    public const string Unknown = "Unknown";

    private static readonly IReadOnlyList<string> Values =
    [
        "KnownIncident",
        "LikelyRegression",
        "SimpleKnownError",
        Unknown,
        "Noise"
    ];

    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.Ordinal);

    public static bool Contains(string value)
    {
        return ValueSet.Contains(value);
    }

    public static JsonArray ToJsonArray()
    {
        var array = new JsonArray();
        foreach (var value in Values)
        {
            array.Add(value);
        }

        return array;
    }
}