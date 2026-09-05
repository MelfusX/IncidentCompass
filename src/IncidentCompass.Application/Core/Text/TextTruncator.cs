namespace IncidentCompass.Application.Core.Text;

internal static class TextTruncator
{
    public static string Truncate(string value, int maxLength, string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        if (suffix.Length >= maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(suffix), "Suffix length must be smaller than maxLength.");
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return suffix.Length == 0
            ? value[..maxLength]
            : value[..(maxLength - suffix.Length)] + suffix;
    }
}
