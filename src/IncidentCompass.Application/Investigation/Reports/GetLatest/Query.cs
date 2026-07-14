using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Investigation.Reports.Get;

namespace IncidentCompass.Application.Investigation.Reports.GetLatest;

public sealed record GetLatestTriageReportQuery(Guid FaultId) : IRequest<TriageReportDetailsResponse>;
