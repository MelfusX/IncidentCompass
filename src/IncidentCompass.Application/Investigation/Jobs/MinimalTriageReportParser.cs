using System.Text.Json;
using IncidentCompass.Application.Investigation.Reports;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class MinimalTriageReportParser
{
    private static readonly HashSet<string> AllowedClassifications = new(
        ["KnownIncident", "LikelyRegression", "SimpleKnownError", "Noise"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedConfidence = new(["Low", "Medium", "High"], StringComparer.Ordinal);

    public static MinimalTriageReport Parse(JsonElement arguments)
    {
        var root = ResolveReportRoot(arguments);
        var statusName = ReadRequiredString(root, "status");
        if (!Enum.TryParse<MinimalTriageReportStatus>(statusName, ignoreCase: false, out var status) ||
            status == MinimalTriageReportStatus.Failed)
        {
            throw new InvalidOperationException("publish_report status must be Completed or InsufficientEvidence.");
        }

        var classification = ReadRequiredString(root, "classification");
        if (status == MinimalTriageReportStatus.InsufficientEvidence)
        {
            if (!string.Equals(classification, "Unknown", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("InsufficientEvidence reports must use classification Unknown.");
            }
        }
        else if (!AllowedClassifications.Contains(classification))
        {
            throw new InvalidOperationException("Completed reports must use a concrete non-Unknown classification.");
        }

        var confidence = ReadRequiredString(root, "confidence");
        if (!AllowedConfidence.Contains(confidence))
        {
            throw new InvalidOperationException($"publish_report confidence '{confidence}' is not supported.");
        }

        return new MinimalTriageReport(
            status,
            ReadRequiredString(root, "summary"),
            classification,
            confidence,
            ReadStringArray(root, "limitations"),
            ReadOptionalString(root, "recommendedNextAction"));
    }

    private static JsonElement ResolveReportRoot(JsonElement arguments)
    {
        if (arguments.TryGetProperty("report_json", out var reportJson))
        {
            return reportJson;
        }

        if (arguments.TryGetProperty("report", out var report))
        {
            return report;
        }

        return arguments;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        var value = ReadOptionalString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"publish_report is missing string {propertyName}.")
            : value;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static IReadOnlyCollection<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(static item => item.GetString()!)
            .ToArray();
    }
}
