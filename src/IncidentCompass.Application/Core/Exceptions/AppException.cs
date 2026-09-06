namespace IncidentCompass.Application.Core.Exceptions;

/// <summary>
/// Base type for exceptions the API error boundary maps to a client response. The exception
/// <see cref="Exception.Message" /> stays developer-facing and is never echoed to a caller.
/// <see cref="Code" /> and <see cref="Detail" /> are the optional, explicitly authored,
/// client-safe alternative a throw site can provide; when absent, the API boundary falls back
/// to a generic code and detail for the exception's mapped status.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, string? code = null, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    protected AppException(string message, Exception? innerException, string? code = null, string? detail = null)
        : base(message, innerException)
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>Stable, machine-readable error code. Null selects the type-level default.</summary>
    public string? Code { get; }

    /// <summary>Authored, client-safe detail text. Null selects the type-level default.</summary>
    public string? Detail { get; }
}
