namespace IncidentCompass.Application.Core.Exceptions;

public sealed class ConflictException : AppException
{
    public ConflictException(string message, string? code = null, string? detail = null)
        : base(message, code, detail)
    {
    }

    public ConflictException(string message, Exception? innerException, string? code = null, string? detail = null)
        : base(message, innerException, code, detail)
    {
    }
}
