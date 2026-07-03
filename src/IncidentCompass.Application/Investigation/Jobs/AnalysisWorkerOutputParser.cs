using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class AnalysisWorkerOutputParser
{
    private static readonly HashSet<string> AllowedClassifications = new(
        ["KnownIncident", "LikelyRegression", "SimpleKnownError", "Unknown", "Noise"],
        StringComparer.Ordinal);

    public static AnalysisWorkerOutput Parse(string content, string roleName)
    {
        var outputLabel = WorkerOutputDiagnosticLabels.Output(roleName);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var keyFacts = ReadKeyFacts(root, outputLabel);
        var classification = ReadRequiredString(root, "candidateClassification", outputLabel);
        if (!AllowedClassifications.Contains(classification))
        {
            throw new InvalidOperationException($"{outputLabel} has unsupported candidateClassification '{classification}'.");
        }

        if (!root.TryGetProperty("needsDeeperContext", out var needsContextElement) ||
            needsContextElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"{outputLabel} is missing boolean needsDeeperContext.");
        }

        var rationale = root.TryGetProperty("rationale", out var rationaleElement) &&
            rationaleElement.ValueKind == JsonValueKind.String
                ? rationaleElement.GetString()
                : null;

        return new AnalysisWorkerOutput(keyFacts, classification, needsContextElement.GetBoolean(), rationale);
    }

    private static IReadOnlyCollection<string> ReadKeyFacts(JsonElement root, string outputLabel)
    {
        if (!root.TryGetProperty("keyFacts", out var keyFactsElement) ||
            keyFactsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{outputLabel} is missing keyFacts array.");
        }

        var keyFacts = new List<string>();
        foreach (var element in keyFactsElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            {
                keyFacts.Add(element.GetString()!);
            }
        }

        return keyFacts.Count > 0
            ? keyFacts
            : throw new InvalidOperationException($"{outputLabel} keyFacts must contain at least one string.");
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, string outputLabel)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"{outputLabel} is missing string {propertyName}.");
        }

        return element.GetString()!;
    }
}