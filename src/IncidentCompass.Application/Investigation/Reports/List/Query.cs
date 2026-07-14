using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Investigation.Reports.List;

public sealed record ListTriageReportsQuery(
    Guid? FaultId,
    string? ServiceName,
    string? Environment,
    string? Status,
    string? Classification,
    int? Limit,
    string? Cursor) : IRequest<TriageReportListResponse>;