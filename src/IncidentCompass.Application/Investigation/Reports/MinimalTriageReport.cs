namespace IncidentCompass.Application.Investigation.Reports;

public sealed record MinimalTriageReport(
    MinimalTriageReportStatus Status,
    string Summary,
    string Classification,
    string Confidence,
    IReadOnlyCollection<string> Limitations,
    string? RecommendedNextAction);
