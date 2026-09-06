namespace IncidentCompass.Application.Core.Exceptions;

public sealed class ForbiddenRequestException : AppException
{
    public ForbiddenRequestException(string message, string? code = null, string? detail = null)
        : base(message, code, detail)
    {
    }

    public ForbiddenRequestException(string message, Exception? innerException, string? code = null, string? detail = null)
        : base(message, innerException, code, detail)
    {
    }
}
