using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.ActionApprovals.Get;

namespace IncidentCompass.Application.Governance.ActionApprovals.Approve;

public sealed class ApproveActionCommandHandler(
    IActionApprovalReviewRepository repository,
    IUserContext userContext) : IRequestHandler<ApproveActionCommand, ActionApprovalDetailsResponse>
{
    public async Task<ActionApprovalDetailsResponse> HandleAsync(
        ApproveActionCommand request,
        CancellationToken cancellationToken)
    {
        var identity = ActionOperatorIdentity.From(userContext);
        var result = await repository.DecideAsync(
            new ActionDecisionRequest(
                request.ActionId,
                identity.TenantId,
                identity.Actor,
                ActionDecisionKind.Approve,
                request.PayloadSha256,
                request.ApprovalSha256,
                null),
            cancellationToken);
        if (result.Outcome == ActionDecisionOutcome.NotFound)
        {
            throw new NotFoundException($"Action approval '{request.ActionId}' was not found.");
        }

        if (result.Outcome == ActionDecisionOutcome.Conflict)
        {
            throw new ConflictException($"Action approval conflict: {result.ConflictCode}.");
        }

        var found = await repository.FindAsync(request.ActionId, identity.TenantId, cancellationToken)
            ?? throw new NotFoundException($"Action approval '{request.ActionId}' was not found.");
        return ActionApprovalResponseMapper.ToDetails(found.Action, found.Provenance);
    }
}
