using FluentValidation.Results;
using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals.List;

public sealed class ListActionApprovalsQueryHandler(
    IActionApprovalReviewRepository repository,
    IUserContext userContext) : IRequestHandler<ListActionApprovalsQuery, ActionApprovalListResponse>
{
    public async Task<ActionApprovalListResponse> HandleAsync(
        ListActionApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        var identity = ActionOperatorIdentity.From(userContext);
        var limit = request.Limit ?? ActionApprovalLimits.DefaultListLimit;
        if (limit is < 1 or > ActionApprovalLimits.MaximumListLimit)
        {
            throw new RequestValidationException([
                new ValidationFailure("limit", $"must be between 1 and {ActionApprovalLimits.MaximumListLimit}.")]);
        }

        var status = ParseStatus(request.Status);
        var cursor = ActionApprovalListCursor.Decode(request.Cursor);
        var rows = await repository.ListAsync(
            new ActionApprovalListFilter(status, cursor?.CreatedAtUtc, cursor?.ActionId, limit + 1),
            identity.TenantId,
            cancellationToken);
        var page = rows.Take(limit).ToArray();
        var items = page.Select(ActionApprovalResponseMapper.ToListItem).ToArray();
        var next = rows.Count > limit
            ? ActionApprovalListCursor.Encode(page[^1].CreatedAtUtc, page[^1].Id)
            : null;
        return new ActionApprovalListResponse(items, next);
    }

    private static ActionApprovalState? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ActionApprovalVocabulary.ParseState(value.Trim().ToLowerInvariant());
        }
        catch (InvalidOperationException)
        {
            throw new RequestValidationException([new ValidationFailure("status", "is invalid.")]);
        }
    }
}
