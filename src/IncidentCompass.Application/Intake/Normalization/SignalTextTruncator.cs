using IncidentCompass.Application.Core.Text;

namespace IncidentCompass.Application.Intake.Normalization;

internal static class SignalTextTruncator
{
    private const int MaxSummaryLength = 500;
    private const int MaxDescriptionLength = 16000;

    public static string TruncateSummary(string value) => TextTruncator.Truncate(value, MaxSummaryLength, "...");

    public static string? TruncateDescription(string? value) =>
        value is null ? null : TextTruncator.Truncate(value, MaxDescriptionLength, "...");
}