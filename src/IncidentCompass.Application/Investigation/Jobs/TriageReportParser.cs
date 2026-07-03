using System.Text.Json;
using IncidentCompass.Application.Investigation.Reports;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class TriageReportParser
{
    private static readonly HashSet<string> AllowedClassifications = new(
        ["KnownIncident", "LikelyRegression", "SimpleKnownError", "Unknown", "Noise"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> AllowedConfidence = new(["Low", "Medium", "High"], StringComparer.Ordinal);

    public static TriageReport Parse(JsonElement arguments)
    {
        var root = ResolveReportRoot(arguments);
        var statusName = ReadRequiredString(root, "status");
        if (!Enum.TryParse<TriageReportStatus>(statusName, ignoreCase: false, out var status) ||
            status == TriageReportStatus.Failed)
        {
            throw new TriageReportValidationException("publish_report status must be Completed or InsufficientEvidence.");
        }

        var classification = ReadRequiredString(root, "classification");
        ValidateClassification(status, classification);

        var confidence = ReadRequiredString(root, "confidence");
        if (!AllowedConfidence.Contains(confidence))
        {
            throw new TriageReportValidationException("publish_report confidence must be Low, Medium, or High.");
        }

        return new TriageReport(
            status,
            ReadRequiredString(root, "summary"),
            classification,
            confidence,
            ReadEvidence(root),
            ReadRequiredStringArray(root, "limitations"),
            ReadRequiredString(root, "recommendedNextAction"));
    }

    private static void ValidateClassification(TriageReportStatus status, string classification)
    {
        if (!AllowedClassifications.Contains(classification))
        {
            throw new TriageReportValidationException("publish_report classification is not supported.");
        }

        if (status == TriageReportStatus.InsufficientEvidence &&
            !string.Equals(classification, "Unknown", StringComparison.Ordinal))
        {
            throw new TriageReportValidationException("InsufficientEvidence reports must use classification Unknown.");
        }

        if (status == TriageReportStatus.Completed &&
            string.Equals(classification, "Unknown", StringComparison.Ordinal))
        {
            throw new TriageReportValidationException("Completed reports must use a concrete non-Unknown classification.");
        }
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

    private static IReadOnlyList<TriageReportEvidenceReference> ReadEvidence(JsonElement root)
    {
        if (!root.TryGetProperty("evidence", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new TriageReportValidationException("publish_report evidence must be an array.");
        }

        var evidence = new List<TriageReportEvidenceReference>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new TriageReportValidationException("publish_report evidence items must be objects.");
            }

            evidence.Add(new TriageReportEvidenceReference(
                ReadRequiredString(item, "referenceId"),
                ReadOptionalString(item, "quote")));
        }

        return evidence;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        var value = ReadOptionalString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new TriageReportValidationException($"publish_report is missing string {propertyName}.")
            : value.Trim();
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new TriageReportValidationException($"publish_report {propertyName} must be a string.");
        }

        return element.GetString();
    }

    private static IReadOnlyList<string> ReadRequiredStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new TriageReportValidationException($"publish_report {propertyName} must be an array of strings.");
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new TriageReportValidationException($"publish_report {propertyName} must contain only strings.");
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }
}
