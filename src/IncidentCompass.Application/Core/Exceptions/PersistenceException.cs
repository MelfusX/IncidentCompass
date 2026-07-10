namespace IncidentCompass.Application.Core.Exceptions;

public sealed class PersistenceException : AppException
{
    public PersistenceException(string operation, Exception innerException)
        : base($"Persistence operation '{operation}' failed.", innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }
}
