namespace IncidentCompass.Application.Tickets;

internal static class TicketTextBounds
{
    public static string Bound(string value, int maximum, bool trim)
    {
        var candidate = trim ? value.Trim() : value;
        if (candidate.Length <= maximum)
        {
            return candidate;
        }

        var length = maximum;
        if (length > 0 &&
            char.IsHighSurrogate(candidate[length - 1]) &&
            char.IsLowSurrogate(candidate[length]))
        {
            length--;
        }

        return candidate[..length];
    }

    public static string? BoundNullable(string? value, int maximum, bool trim) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value, maximum, trim);
}
