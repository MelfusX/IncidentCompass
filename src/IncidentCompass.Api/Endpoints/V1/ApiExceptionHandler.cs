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
        var mapped = MapException(exception);
        if (mapped is not { } value)
        {
            return false;
        }

        LogMappedException(httpContext, exception, value.Code);
        await value.Result.ExecuteAsync(httpContext);
        return true;
    }

    private static MappedApiError? MapException(Exception exception)
    {
        return exception switch
        {
            NotFoundException current => ApiErrorMapping.NotFound(current),
            ConflictException current => ApiErrorMapping.Conflict(current),
            ForbiddenRequestException current => ApiErrorMapping.Forbidden(current),
            RequestValidationException current => ApiErrorMapping.RequestValidation(current),
            ValidationException current => ApiErrorMapping.BadRequest(current),
            DomainException => ApiErrorMapping.InternalDomainViolation(),
            _ => null
        };
    }

    // Every mapped exception is logged here, once, with the correlation id an operator would use
    // to find the request and the stable code that was returned to the caller. AppException and
    // DomainException messages are authored by this codebase (not raw provider or infrastructure
    // text), so attaching the exception itself is safe and matches the precedent already set for
    // domain exceptions reaching this boundary.
    private void LogMappedException(HttpContext httpContext, Exception exception, string errorCode)
    {
        var correlationId = httpContext.TraceIdentifier;
        if (exception is DomainException)
        {
            LogDomainExceptionReachedBoundary(logger, exception, correlationId, errorCode);
            return;
        }

        LogClientExceptionReachedBoundary(logger, exception, correlationId, errorCode);
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Error,
        Message = "A domain exception reached the API error boundary with correlation id {CorrelationId} and error code {ErrorCode}.")]
    private static partial void LogDomainExceptionReachedBoundary(
        ILogger logger,
        Exception exception,
        string correlationId,
        string errorCode);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "A client-facing exception reached the API error boundary with correlation id {CorrelationId} and error code {ErrorCode}.")]
    private static partial void LogClientExceptionReachedBoundary(
        ILogger logger,
        Exception exception,
        string correlationId,
        string errorCode);
}
