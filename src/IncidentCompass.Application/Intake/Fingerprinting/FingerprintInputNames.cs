namespace IncidentCompass.Application.Intake.Fingerprinting;

public static class FingerprintInputNames
{
    public const string ServiceName = "ServiceName";
    public const string Environment = "Environment";
    public const string ErrorType = "ErrorType";
    public const string ErrorMessage = "ErrorMessage";
    public const string RouteOrOperation = "RouteOrOperation";
    public const string OperationName = "OperationName";
    public const string HttpRoute = "HttpRoute";
    public const string Severity = "Severity";
    public const string Source = "Source";

    public static IReadOnlyList<string> Default { get; } =
    [
        ServiceName,
        Environment,
        ErrorType,
        ErrorMessage,
        RouteOrOperation
    ];

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ServiceName,
        Environment,
        ErrorType,
        ErrorMessage,
        RouteOrOperation,
        OperationName,
        HttpRoute,
        Severity,
        Source
    };
}