namespace IncidentCompass.Application.Intake.Normalization;

internal static class SummarySynthesizer
{
    private const int MaxLength = 500;
    private const int TruncatedLength = MaxLength - 3;

    public static string ForStructuredSignal(
        string serviceName,
        string? operationName,
        string? httpRoute,
        string? errorType,
        string? errorMessage)
    {
        var operationDescriptor = operationName ?? httpRoute ?? "operation";
        var summary = $"{serviceName}: {operationDescriptor} failed";

        var hasErrorType = !string.IsNullOrWhiteSpace(errorType);
        var hasErrorMessage = !string.IsNullOrWhiteSpace(errorMessage);
        if (hasErrorType || hasErrorMessage)
        {
            var detail = hasErrorType && hasErrorMessage
                ? $"{errorType}: {errorMessage}"
                : hasErrorType
                    ? errorType!
                    : errorMessage!;
            summary += $" - {detail}";
        }

        return Truncate(summary);
    }

    private static string Truncate(string value)
    {
        return value.Length > MaxLength
            ? value[..TruncatedLength] + "..."
            : value;
    }
}
