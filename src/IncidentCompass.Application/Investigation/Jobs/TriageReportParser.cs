using System.Text.Json;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Investigation.Reports;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class TriageReportParser
{
    private static readonly HashSet<string> AllowedConfidence = new(["Low", "Medium", "High"], StringComparer.Ordinal);

    public static TriageReport Parse(JsonElement arguments)
    {
        var root = ResolveReportRoot(arguments);
        var statusName = ReadReportString(root, "status");
        if (!Enum.IsDefined(typeof(TriageReportStatus), statusName) ||
            !Enum.TryParse<TriageReportStatus>(statusName, ignoreCase: false, out var status) ||
            status == TriageReportStatus.Failed)
        {
            throw new TriageReportValidationException("publish_report status must be Completed or InsufficientEvidence.");
        }

        var classification = ReadReportString(root, "classification");
        ValidateClassification(status, classification);

        var confidence = ReadReportString(root, "confidence");
        if (!AllowedConfidence.Contains(confidence))
        {
            throw new TriageReportValidationException("publish_report confidence must be Low, Medium, or High.");
        }

        var documentationFit = ReadDocumentationFit(root);
        var evidence = ReadEvidence(root);
        if (status == TriageReportStatus.Completed && evidence.Count == 0)
        {
            throw new TriageReportValidationException("Completed reports must include at least one evidence item.");
        }

        return new TriageReport(
            status,
            ReadReportString(root, "summary"),
            classification,
            confidence,
            evidence,
            ReadRequiredStringArray(root, "limitations"),
            ReadReportString(root, "recommendedNextAction"))
        {
            DocumentationFit = documentationFit
        };
    }

    private static void ValidateClassification(TriageReportStatus status, string classification)
    {
        if (!TriageClassificationVocabulary.Contains(classification))
        {
            throw new TriageReportValidationException("publish_report classification is not supported.");
        }

        if (status == TriageReportStatus.InsufficientEvidence &&
            !string.Equals(classification, TriageClassificationVocabulary.Unknown, StringComparison.Ordinal))
        {
            throw new TriageReportValidationException("InsufficientEvidence reports must use classification Unknown.");
        }

        if (status == TriageReportStatus.Completed &&
            string.Equals(classification, TriageClassificationVocabulary.Unknown, StringComparison.Ordinal))
        {
            throw new TriageReportValidationException("Completed reports must use a concrete non-Unknown classification.");
        }
    }

    private static DocumentationFitStatus ReadDocumentationFit(JsonElement root)
    {
        var documentationFitName = ReadReportString(root, "documentationFit");
        if (!Enum.TryParse<DocumentationFitStatus>(documentationFitName, ignoreCase: false, out var documentationFit) ||
            !Enum.IsDefined(documentationFit))
        {
            throw new TriageReportValidationException(
                "publish_report documentationFit must be Current, CurrentWithHistorical, StaleOnly, Missing, or MultipleCurrentDocuments.");
        }

        return documentationFit;
    }

    private static JsonElement ResolveReportRoot(JsonElement arguments)
    {
        JsonElementReader.RequireObject(
            arguments,
            "publish_report arguments must be an object.",
            CreateException);

        if (arguments.TryGetProperty("report_json", out var reportJson))
        {
            return JsonElementReader.RequireObject(
                reportJson,
                "publish_report report_json must be an object.",
                CreateException);
        }

        if (arguments.TryGetProperty("report", out var report))
        {
            return JsonElementReader.RequireObject(
                report,
                "publish_report report must be an object.",
                CreateException);
        }

        return arguments;
    }

    private static List<TriageReportEvidenceReference> ReadEvidence(JsonElement root)
    {
        if (!root.TryGetProperty("evidence", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new TriageReportValidationException("publish_report evidence must be an array.");
        }

        var evidence = new List<TriageReportEvidenceReference>();
        foreach (var item in element.EnumerateArray())
        {
            JsonElementReader.RequireObject(
                item,
                "publish_report evidence items must be objects.",
                CreateException);

            evidence.Add(new TriageReportEvidenceReference(
                ReadReportString(item, "referenceId"),
                JsonElementReader.ReadOptionalString(
                    item,
                    "quote",
                    CreateException,
                    "publish_report quote must be a string.")));
        }

        return evidence;
    }

    private static string ReadReportString(JsonElement root, string propertyName)
    {
        return JsonElementReader.ReadRequiredString(
            root,
            propertyName,
            $"publish_report is missing string {propertyName}.",
            CreateException,
            $"publish_report {propertyName} must be a string.");
    }

    private static List<string> ReadRequiredStringArray(JsonElement root, string propertyName)
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

    private static Exception CreateException(string message)
    {
        return new TriageReportValidationException(message);
    }
}
