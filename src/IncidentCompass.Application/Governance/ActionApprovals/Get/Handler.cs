using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Application.Governance.ActionApprovals.Get;

public sealed class GetActionApprovalQueryHandler(
    IActionApprovalReviewRepository repository,
    IUserContext userContext) : IRequestHandler<GetActionApprovalQuery, ActionApprovalDetailsResponse>
{
    public async Task<ActionApprovalDetailsResponse> HandleAsync(
        GetActionApprovalQuery request,
        CancellationToken cancellationToken)
    {
        var identity = ActionOperatorIdentity.From(userContext);
        var found = await repository.FindAsync(request.ActionId, identity.TenantId, cancellationToken)
            ?? throw new NotFoundException($"Action approval '{request.ActionId}' was not found.");
        return ActionApprovalResponseMapper.ToDetails(found.Action, found.Provenance);
    }
}
