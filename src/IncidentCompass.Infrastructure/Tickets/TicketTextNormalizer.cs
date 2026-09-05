using System.Globalization;
using System.Text;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class TicketTextNormalizer
{
    public static string NormalizePhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(rune.ToString());
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> DistinctTokens(string? value, int maximum)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new List<string>();
        foreach (var token in NormalizePhrase(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 3 && seen.Add(token))
            {
                tokens.Add(token);
                if (tokens.Count == maximum)
                {
                    break;
                }
            }
        }

        return tokens;
    }

    public static bool ContainsWholePhrase(string haystack, string? phrase)
    {
        var needle = NormalizePhrase(phrase);
        return needle.Length > 0 &&
            $" {NormalizePhrase(haystack)} ".Contains($" {needle} ", StringComparison.Ordinal);
    }
}
