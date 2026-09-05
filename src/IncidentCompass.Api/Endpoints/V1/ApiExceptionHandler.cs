using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace IncidentCompass.Api;

internal sealed partial class ApiExceptionHandler(
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
            LogDomainExceptionReachedBoundary(logger, exception);
        }

        return exception switch
        {
            NotFoundException current => ApiErrorMapping.NotFound(current),
            ConflictException current => ApiErrorMapping.Conflict(current),
            ForbiddenRequestException current => ApiErrorMapping.Forbidden(current),
            RequestValidationException current => ApiErrorMapping.RequestValidation(current),
            ValidationException current => ApiErrorMapping.BadRequest(current.Message),
            DomainException current => ApiErrorMapping.InternalDomainViolation(current),
            _ => null
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A domain exception reached the API error boundary.")]
    private static partial void LogDomainExceptionReachedBoundary(ILogger logger, Exception exception);
}
