using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Application.Governance.ActionApprovals;

internal sealed record ActionOperatorIdentity(string TenantId, string Actor)
{
    public static ActionOperatorIdentity From(IUserContext context)
    {
        if (!context.IsAuthenticated || string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId))
        {
            throw new ForbiddenRequestException("An authenticated action operator is required.");
        }

        return new ActionOperatorIdentity(context.TenantId, "key:" + context.UserId);
    }
}
