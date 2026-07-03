using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed record TriageReportDetailsResponse(
    Guid Id,
    Guid FaultId,
    string Status,
    string Summary,
    string Classification,
    string Confidence,
    bool? IsMassIssue,
    string RecommendedNextAction,
    IReadOnlyList<string> Limitations,
    string ConfigHash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TriageReportEvidenceResponse> Evidence);
