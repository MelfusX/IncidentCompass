namespace IncidentCompass.Application.Core.Exceptions;

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message, string? code = null, string? detail = null)
        : base(message, code, detail)
    {
    }

    public NotFoundException(string message, Exception? innerException, string? code = null, string? detail = null)
        : base(message, innerException, code, detail)
    {
    }
}
