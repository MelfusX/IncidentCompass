namespace IncidentCompass.Application.Intake.Normalization;

internal static class SignalTextTruncator
{
    private const int MaxSummaryLength = 500;
    private const int MaxDescriptionLength = 16000;

    public static string TruncateSummary(string value) => Truncate(value, MaxSummaryLength);

    public static string? TruncateDescription(string? value) =>
        value is null ? null : Truncate(value, MaxDescriptionLength);

    private static string Truncate(string value, int maxLength)
    {
        return value.Length > maxLength
            ? value[..(maxLength - 3)] + "..."
            : value;
    }
}
