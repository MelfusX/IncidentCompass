using System.Text.Json;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class AnalysisWorkerOutputParser
{
    public static AnalysisWorkerOutput Parse(string content, string roleName)
    {
        var outputLabel = WorkerOutputDiagnosticLabels.Output(roleName);
        using var document = JsonDocument.Parse(content);
        var root = JsonElementReader.RequireObject(
            document.RootElement,
            $"{outputLabel} must be an object.",
            CreateException);
        var keyFacts = ReadKeyFacts(root, outputLabel);
        var classification = JsonElementReader.ReadRequiredString(
            root,
            "candidateClassification",
            $"{outputLabel} is missing string candidateClassification.",
            CreateException,
            trim: false);
        if (!TriageClassificationVocabulary.Contains(classification))
        {
            throw new InvalidOperationException($"{outputLabel} has unsupported candidateClassification '{classification}'.");
        }

        if (!root.TryGetProperty("needsDeeperContext", out var needsContextElement) ||
            needsContextElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"{outputLabel} is missing boolean needsDeeperContext.");
        }

        var rationale = JsonElementReader.ReadOptionalString(root, "rationale", CreateException);
        return new AnalysisWorkerOutput(keyFacts, classification, needsContextElement.GetBoolean(), rationale);
    }

    private static List<string> ReadKeyFacts(JsonElement root, string outputLabel)
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

        return keyFacts;
    }

    private static Exception CreateException(string message)
    {
        return new InvalidOperationException(message);
    }
}
