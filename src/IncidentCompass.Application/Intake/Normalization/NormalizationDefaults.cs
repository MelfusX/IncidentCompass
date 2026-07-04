namespace IncidentCompass.Application.Intake.Normalization;

internal static class NormalizationDefaults
{
    public const string Unknown = "unknown";

    public static string UnknownIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unknown : value;
    }
}