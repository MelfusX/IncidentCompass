using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
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

    public static IResult NotFound(NotFoundException exception) => NotFound(exception.Message);

    public static IResult Conflict(ConflictException exception) =>
        Problem("Conflict", exception.Message, StatusCodes.Status409Conflict);

    public static IResult Forbidden(ForbiddenRequestException exception) =>
        Problem("Forbidden", exception.Message, StatusCodes.Status403Forbidden);

    public static IResult NotFound(string error)
    {
        return Problem(
            "Not found",
            error,
            StatusCodes.Status404NotFound);
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
}
