using Microsoft.AspNetCore.Authorization;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyAuthorizationHandler(ApiKeyCredentialResolver resolver)
    : AuthorizationHandler<ApiKeyAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiKeyAuthorizationRequirement requirement)
    {
        if (!resolver.AuthenticationRequired ||
            context.User.Identity?.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
