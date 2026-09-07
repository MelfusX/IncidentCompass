using Microsoft.AspNetCore.Authorization;

namespace IncidentCompass.Api.Security;

internal sealed class ActionOperatorAuthorizationHandler(ApiKeyRuntimeSettings settings)
    : AuthorizationHandler<ActionOperatorAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActionOperatorAuthorizationRequirement requirement)
    {
        var isApiKey = context.User.Identity?.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme;
        var hasKeyId = !string.IsNullOrWhiteSpace(
            context.User.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value);
        var hasTenant = !string.IsNullOrWhiteSpace(
            context.User.FindFirst(ApiKeyAuthenticationDefaults.TenantIdClaim)?.Value);
        if (settings.Enabled && isApiKey && hasKeyId && hasTenant)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
