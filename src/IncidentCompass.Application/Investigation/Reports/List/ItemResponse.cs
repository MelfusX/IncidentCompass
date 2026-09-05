namespace IncidentCompass.Application.Investigation.Reports.List;

public sealed record TriageReportListItemResponse(
    Guid Id,
    Guid FaultId,
    string Status,
    string Summary,
    string Classification,
    string Confidence,
    bool? IsMassIssue,
    string RecommendedNextAction,
    DateTimeOffset CreatedAtUtc,
    string ServiceName,
    string Environment,
    Guid? SupersedesReportId,
    Guid? SupersededByReportId,
    bool IsLatestForFault);
