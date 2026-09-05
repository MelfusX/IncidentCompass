using IncidentCompass.Application.Core.Security;
using IncidentCompass.Application.Core.Tenancy;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyIncidentTenantContext(IUserContext userContext) : IIncidentTenantContext
{
    public Task<string> GetTenantIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!userContext.IsAuthenticated || string.IsNullOrEmpty(userContext.TenantId))
        {
            throw new InvalidOperationException("An authenticated API tenant is required.");
        }

        return Task.FromResult(userContext.TenantId);
    }
}
