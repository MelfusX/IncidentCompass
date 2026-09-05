using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Governance.ActionApprovals.Get;

public sealed record GetActionApprovalQuery(Guid ActionId) : IRequest<ActionApprovalDetailsResponse>;
