using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Governance.ActionApprovals.Get;

namespace IncidentCompass.Application.Governance.ActionApprovals.Reject;

public sealed record RejectActionCommand(
    Guid ActionId,
    string PayloadSha256,
    string ApprovalSha256,
    string? Reason) : IRequest<ActionApprovalDetailsResponse>;
