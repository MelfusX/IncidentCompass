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
                "documentationFit": "Current",
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
                "documentationFit": "Missing",
                "evidence": [],
                "limitations": ["No matching memory."],
                "recommendedNextAction": "Collect more context."
              }
            }
            """);

        var report = TriageReportParser.Parse(arguments.RootElement);

        Assert.Equal(TriageReportStatus.InsufficientEvidence, report.Status);
        Assert.Equal(DocumentationFitStatus.Missing, report.DocumentationFit);
        Assert.Empty(report.Evidence);
    }

    [Theory]
    [InlineData("Current", DocumentationFitStatus.Current)]
    [InlineData("CurrentWithHistorical", DocumentationFitStatus.CurrentWithHistorical)]
    [InlineData("StaleOnly", DocumentationFitStatus.StaleOnly)]
    [InlineData("Missing", DocumentationFitStatus.Missing)]
    [InlineData("MultipleCurrentDocuments", DocumentationFitStatus.MultipleCurrentDocuments)]
    public void Parse_DocumentationFitStatus_ReturnsTypedValue(string documentationFit, DocumentationFitStatus expected)
    {
        using var arguments = JsonDocument.Parse($$"""
            {
              "report_json": {
                "status": "Completed",
                "summary": "Documentation assessment.",
                "classification": "SimpleKnownError",
                "confidence": "Medium",
                "documentationFit": "{{documentationFit}}",
                "evidence": [{ "referenceId": "artifact:00000000-0000-0000-0000-000000000001" }],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        var report = TriageReportParser.Parse(arguments.RootElement);

        Assert.Equal(expected, report.DocumentationFit);
    }

    [Theory]
    [InlineData("Unsupported")]
    [InlineData("current")]
    public void Parse_InvalidDocumentationFit_ThrowsValidationException(string documentationFit)
    {
        using var arguments = JsonDocument.Parse($$"""
            {
              "report_json": {
                "status": "Completed",
                "summary": "Documentation assessment.",
                "classification": "SimpleKnownError",
                "confidence": "Medium",
                "documentationFit": "{{documentationFit}}",
                "evidence": [{ "referenceId": "artifact:00000000-0000-0000-0000-000000000001" }],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        var exception = Assert.Throws<TriageReportValidationException>(() => TriageReportParser.Parse(arguments.RootElement));

        Assert.Contains("documentationFit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingDocumentationFit_ThrowsValidationException()
    {
        using var arguments = JsonDocument.Parse("""
            {
              "report_json": {
                "status": "Completed",
                "summary": "Documentation assessment.",
                "classification": "SimpleKnownError",
                "confidence": "Medium",
                "evidence": [{ "referenceId": "artifact:00000000-0000-0000-0000-000000000001" }],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        var exception = Assert.Throws<TriageReportValidationException>(() => TriageReportParser.Parse(arguments.RootElement));

        Assert.Contains("documentationFit", exception.Message, StringComparison.Ordinal);
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
                "documentationFit": "Current",
                "evidence": [{ "referenceId": "artifact:00000000-0000-0000-0000-000000000001" }],
                "limitations": [],
                "recommendedNextAction": "Review."
              }
            }
            """);

        Assert.Throws<TriageReportValidationException>(() => TriageReportParser.Parse(arguments.RootElement));
    }
}
