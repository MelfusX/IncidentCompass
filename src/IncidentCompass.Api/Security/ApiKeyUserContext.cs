using System.Security.Claims;
using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme;

    public string? UserId => IsAuthenticated ? Principal?.FindFirstValue(ApiKeyAuthenticationDefaults.KeyIdClaim) : null;

    public string? TenantId => IsAuthenticated ? Principal?.FindFirstValue(ApiKeyAuthenticationDefaults.TenantIdClaim) : null;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Groups => [];
}
