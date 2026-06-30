using IncidentCompass.Application.Core.Errors;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace IncidentCompass.Api;

internal sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var result = MapException(exception);
        if (result is null)
        {
            return false;
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }

    private IResult? MapException(Exception exception)
    {
        if (exception is DomainException)
        {
            logger.LogError(exception, "A domain exception reached the API error boundary.");
        }

        return exception switch
        {
            UnauthorizedRequestException current => ApiErrorMapping.Unauthorized(current),
            ForbiddenRequestException current => ApiErrorMapping.Forbidden(current),
            NotFoundException current => ApiErrorMapping.NotFound(current),
            ConflictException current => ApiErrorMapping.Conflict(current),
            ProviderException current => ApiErrorMapping.ProviderProblem(current),
            ValidationException current => ApiErrorMapping.BadRequest(current.Message),
            DomainException current => ApiErrorMapping.InternalDomainViolation(current),
            _ => null
        };
    }
}
