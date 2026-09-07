using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Governance.ActionApprovals.Get;

namespace IncidentCompass.Application.Governance.ActionApprovals.Reject;

public sealed class RejectActionCommandHandler(
    IActionApprovalReviewRepository repository,
    IUserContext userContext) : IRequestHandler<RejectActionCommand, ActionApprovalDetailsResponse>
{
    public async Task<ActionApprovalDetailsResponse> HandleAsync(
        RejectActionCommand request,
        CancellationToken cancellationToken)
    {
        var identity = ActionOperatorIdentity.From(userContext);
        var result = await repository.DecideAsync(
            new ActionDecisionRequest(
                request.ActionId,
                identity.TenantId,
                identity.Actor,
                ActionDecisionKind.Reject,
                request.PayloadSha256,
                request.ApprovalSha256,
                request.Reason),
            cancellationToken);
        if (result.Outcome == ActionDecisionOutcome.NotFound)
        {
            throw new NotFoundException(
                $"Action approval '{request.ActionId}' was not found.",
                ApplicationErrorCodes.ActionApprovalNotFound,
                "The requested action approval does not exist.");
        }

        if (result.Outcome == ActionDecisionOutcome.Conflict)
        {
            throw new ConflictException(
                $"Action approval conflict: {result.ConflictCode}.",
                ApplicationErrorCodes.ActionApprovalConflictCodePrefix + result.ConflictCode,
                "The action approval is no longer in the expected state.");
        }

        var found = await repository.FindAsync(request.ActionId, identity.TenantId, cancellationToken)
            ?? throw new NotFoundException(
                $"Action approval '{request.ActionId}' was not found.",
                ApplicationErrorCodes.ActionApprovalNotFound,
                "The requested action approval does not exist.");
        return ActionApprovalResponseMapper.ToDetails(found.Action, found.Provenance);
    }
}
