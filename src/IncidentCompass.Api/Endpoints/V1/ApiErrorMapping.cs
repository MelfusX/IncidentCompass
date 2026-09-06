using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;

namespace IncidentCompass.Api;

/// <summary>
/// The single place that owns the mapping from a caught exception type to an HTTP status, a
/// stable machine-readable error code and an authored client-safe detail. An exception's own
/// <see cref="Exception.Message" /> is never placed in a response: it is developer-facing and
/// can change wording or carry internal detail at any time. When a throw site attaches an
/// explicit <see cref="AppException.Code" />/<see cref="AppException.Detail" /> (see
/// <see cref="ApplicationErrorCodes" />), that authored pair is used; otherwise the generic
/// per-status default below applies.
/// </summary>
internal static class ApiErrorMapping
{
    public const string RequestValidationFailedCode = "request_validation_failed";
    public const string ResourceNotFoundCode = "resource_not_found";
    public const string ResourceConflictCode = "resource_conflict";
    public const string RequestForbiddenCode = "request_forbidden";
    public const string InternalDomainViolationCode = "internal_domain_violation";

    private const string ResourceNotFoundDetail = "The requested resource does not exist.";
    private const string ResourceConflictDetail = "The request conflicts with the current state of the resource.";
    private const string RequestForbiddenDetail = "The request is not permitted.";
    private const string RequestValidationFailedDetail = "The request failed validation.";

    public static MappedApiError BadRequest(ValidationException exception) =>
        Map(
            "Request validation failed",
            exception.Code,
            RequestValidationFailedCode,
            exception.Detail,
            RequestValidationFailedDetail,
            StatusCodes.Status400BadRequest);

    public static MappedApiError RequestValidation(RequestValidationException exception)
    {
        var errors = exception.Failures
            .GroupBy(static failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        var result = Results.ValidationProblem(
            errors,
            title: "Request validation failed",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: ErrorCodeExtension(RequestValidationFailedCode));
        return new MappedApiError(result, RequestValidationFailedCode);
    }

    public static MappedApiError NotFound(NotFoundException exception) =>
        Map(
            "Not found",
            exception.Code,
            ResourceNotFoundCode,
            exception.Detail,
            ResourceNotFoundDetail,
            StatusCodes.Status404NotFound);

    public static MappedApiError Conflict(ConflictException exception) =>
        Map(
            "Conflict",
            exception.Code,
            ResourceConflictCode,
            exception.Detail,
            ResourceConflictDetail,
            StatusCodes.Status409Conflict);

    public static MappedApiError Forbidden(ForbiddenRequestException exception) =>
        Map(
            "Forbidden",
            exception.Code,
            RequestForbiddenCode,
            exception.Detail,
            RequestForbiddenDetail,
            StatusCodes.Status403Forbidden);

    public static MappedApiError InternalDomainViolation()
    {
        var result = Problem(
            "Domain invariant violation",
            "The request could not be completed.",
            StatusCodes.Status500InternalServerError,
            InternalDomainViolationCode);
        return new MappedApiError(result, InternalDomainViolationCode);
    }

    private static MappedApiError Map(
        string title,
        string? explicitCode,
        string defaultCode,
        string? explicitDetail,
        string defaultDetail,
        int statusCode)
    {
        var code = explicitCode ?? defaultCode;
        var detail = explicitDetail ?? defaultDetail;
        return new MappedApiError(Problem(title, detail, statusCode, code), code);
    }

    private static IResult Problem(string title, string detail, int statusCode, string errorCode)
    {
        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: ErrorCodeExtension(errorCode));
    }

    private static Dictionary<string, object?> ErrorCodeExtension(string errorCode) =>
        new() { ["errorCode"] = errorCode };
}
