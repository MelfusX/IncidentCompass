using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace IncidentCompass.Api.Security;

internal sealed class ActionOperatorAuthorizationResultHandler(ApiKeyRuntimeSettings settings)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!settings.Enabled &&
            policy.Requirements.Any(static requirement => requirement is ActionOperatorAuthorizationRequirement))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
