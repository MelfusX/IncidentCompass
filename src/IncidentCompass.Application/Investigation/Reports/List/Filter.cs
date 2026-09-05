namespace IncidentCompass.Application.Investigation.Reports.List;

public sealed record TriageReportListFilter(
    Guid? FaultId,
    string? ServiceName,
    string? Environment,
    string? Status,
    string? Classification,
    DateTimeOffset? BeforeCreatedAtUtc,
    Guid? BeforeReportId,
    int Limit);
