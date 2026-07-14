namespace IncidentCompass.Application.Investigation.Reports.List;

public sealed record TriageReportListResponse(
    IReadOnlyList<TriageReportListItemResponse> Reports,
    string? NextCursor);