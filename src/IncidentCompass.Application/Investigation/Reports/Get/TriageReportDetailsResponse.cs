namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed record TriageReportDetailsResponse(
    Guid Id,
    Guid FaultId,
    string Status,
    string Summary,
    string Classification,
    string Confidence,
    string DocumentationFit,
    bool? IsMassIssue,
    string RecommendedNextAction,
    IReadOnlyList<string> Limitations,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    Guid? SupersedesReportId,
    Guid? SupersededByReportId,
    bool IsLatestForFault,
    IReadOnlyList<TriageReportEvidenceResponse> Evidence);
