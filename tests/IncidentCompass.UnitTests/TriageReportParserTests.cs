using System.Text.Json;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports;

namespace IncidentCompass.UnitTests;

public sealed class TriageReportParserTests
{
    [Fact]
    public void Parse_CompletedWithoutEvidence_ThrowsValidationException()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "report_json": {
                "status": "Completed",
                "summary": "No evidence report.",
                "classification": "SimpleKnownError",
                "confidence": "Medium",
                "evidence": [],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        var exception = Assert.Throws<TriageReportValidationException>(() => TriageReportParser.Parse(arguments.RootElement));

        Assert.Contains("at least one evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InsufficientEvidenceWithoutEvidence_ReturnsReport()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "report_json": {
                "status": "InsufficientEvidence",
                "summary": "Not enough grounded context.",
                "classification": "Unknown",
                "confidence": "Low",
                "evidence": [],
                "limitations": ["No matching memory."],
                "recommendedNextAction": "Collect more context."
              }
            }
            """);

        var report = TriageReportParser.Parse(arguments.RootElement);

        Assert.Equal(TriageReportStatus.InsufficientEvidence, report.Status);
        Assert.Empty(report.Evidence);
    }

    [Fact]
    public void Parse_NumericStatusString_ThrowsValidationException()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "report_json": {
                "status": "1",
                "summary": "Numeric status.",
                "classification": "SimpleKnownError",
                "confidence": "Medium",
                "evidence": [{ "referenceId": "artifact:00000000-0000-0000-0000-000000000001" }],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        Assert.Throws<TriageReportValidationException>(() => TriageReportParser.Parse(arguments.RootElement));
    }
}
