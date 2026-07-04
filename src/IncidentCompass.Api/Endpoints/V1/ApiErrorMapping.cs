using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Errors;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Domain.Exceptions;

namespace IncidentCompass.Api;

internal static class ApiErrorMapping
{
    public static IResult BadRequest(string error) =>
        Problem("Request validation failed", error, StatusCodes.Status400BadRequest);

    public static IResult RequestValidation(RequestValidationException exception)
    {
        var errors = exception.Failures
            .GroupBy(static failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Results.ValidationProblem(
            errors,
            title: "Request validation failed",
            statusCode: StatusCodes.Status400BadRequest);
    }

    public static IResult Unauthorized(UnauthorizedRequestException exception)
    {
        return Problem(
            "Unauthorized",
            exception.Message,
            StatusCodes.Status401Unauthorized);
    }

    public static IResult Forbidden(ForbiddenRequestException exception)
    {
        return Problem(
            "Forbidden",
            exception.Message,
            StatusCodes.Status403Forbidden);
    }

    public static IResult NotFound(NotFoundException exception) => NotFound(exception.Message);

    public static IResult NotFound(string error)
    {
        return Problem(
            "Not found",
            error,
            StatusCodes.Status404NotFound);
    }

    public static IResult Conflict(ConflictException exception) =>
        Problem("Conflict", exception.Message, StatusCodes.Status409Conflict);

    public static IResult ProviderProblem(ProviderException exception)
    {
        return Problem(
            ToProviderTitle(exception),
            ToProviderDetail(exception),
            StatusCodes.Status502BadGateway,
            new Dictionary<string, object?>
            {
                ["provider"] = exception.Provider,
                ["errorCode"] = ToPublicProviderErrorCode(exception),
                ["providerStatusCode"] = exception.StatusCode is null
                    ? null
                    : (int)exception.StatusCode
            });
    }

    public static IResult InternalDomainViolation(DomainException exception)
    {
        return Problem(
            "Domain invariant violation",
            "The request could not be completed.",
            StatusCodes.Status500InternalServerError);
    }

    private static IResult Problem(
        string title,
        string detail,
        int statusCode,
        IDictionary<string, object?>? extensions = null)
    {
        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: extensions);
    }

    private static string ToProviderTitle(ProviderException exception)
    {
        return exception switch
        {
            AiModelException => "Model provider request failed",
            _ => "Embedding provider request failed"
        };
    }

    private static string ToProviderDetail(ProviderException exception)
    {
        return exception switch
        {
            AiModelException => "The upstream model provider request failed.",
            _ => "The upstream embedding provider request failed."
        };
    }

    private static string ToPublicProviderErrorCode(ProviderException exception)
    {
        return exception switch
        {
            AiModelException => ToPublicModelErrorCode(exception.ErrorCode),
            _ => ToPublicEmbeddingErrorCode(exception.ErrorCode)
        };
    }

    private static string ToPublicModelErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "authentication_error" or
            "configuration_error" or
            "empty_response" or
            "invalid_json" or
            "invalid_request" or
            "provider_timeout" or
            "provider_unavailable" or
            "rate_limited" or
            "timeout" or
            "transport_error" => errorCode,
            _ => "provider_error"
        };
    }

    private static string ToPublicEmbeddingErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "authentication_error" or
            "configuration_error" or
            "empty_embedding" or
            "invalid_embedding" or
            "invalid_json" or
            "invalid_request" or
            "provider_timeout" or
            "provider_unavailable" or
            "rate_limited" or
            "timeout" or
            "transport_error" => errorCode,
            _ => "provider_error"
        };
    }
}
