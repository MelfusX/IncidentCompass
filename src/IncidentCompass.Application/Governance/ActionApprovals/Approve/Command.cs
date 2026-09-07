using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals.Get;

namespace IncidentCompass.Application.Governance.ActionApprovals.Approve;

public sealed record ApproveActionCommand(
    Guid ActionId,
    string PayloadSha256,
    string ApprovalSha256) : IRequest<ActionApprovalDetailsResponse>;
