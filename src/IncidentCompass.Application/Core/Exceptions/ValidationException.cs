namespace IncidentCompass.Application.Core.Exceptions;

public abstract class ValidationException : AppException
{
    protected ValidationException(string message, string? code = null, string? detail = null)
        : base(message, code, detail)
    {
    }

    protected ValidationException(string message, Exception? innerException, string? code = null, string? detail = null)
        : base(message, innerException, code, detail)
    {
    }
}
