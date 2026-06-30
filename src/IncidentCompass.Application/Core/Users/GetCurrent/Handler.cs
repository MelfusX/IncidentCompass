using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Application.Core.Users;

public sealed class GetCurrentUserHandler(IUserContext userContext)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public Task<CurrentUserDto> HandleAsync(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var user = new CurrentUserDto(
            userContext.IsAuthenticated,
            userContext.UserId,
            userContext.TenantId,
            userContext.Roles,
            userContext.Groups);

        return Task.FromResult(user);
    }
}
