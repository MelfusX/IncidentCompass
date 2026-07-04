namespace IncidentCompass.Application.Intake.Normalization;

internal static class SummarySynthesizer
{
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

        return SignalTextTruncator.TruncateSummary(summary);
    }
}
