namespace IncidentCompass.Application.Investigation.Reports;

public sealed record TriageReport(
    TriageReportStatus Status,
    string Summary,
    string Classification,
    string Confidence,
    IReadOnlyList<TriageReportEvidenceReference> Evidence,
    IReadOnlyList<string> Limitations,
    string RecommendedNextAction);
