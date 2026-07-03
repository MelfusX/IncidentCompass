using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Investigation.Reports.Get;

public sealed record GetTriageReportQuery(Guid ReportId) : IRequest<TriageReportDetailsResponse>;
