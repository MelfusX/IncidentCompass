using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Intake.GetFault;

public sealed record GetFaultQuery(Guid FaultId) : IRequest<FaultDetailsResponse>;
