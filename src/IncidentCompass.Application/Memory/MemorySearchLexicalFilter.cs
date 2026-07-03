namespace IncidentCompass.Application.Memory;

internal static class MemorySearchLexicalFilter
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "api", "as", "by", "error", "event", "exception", "for",
        "from", "in", "into", "is", "it", "of", "on", "or", "post", "request", "requests",
        "service", "the", "this", "to", "while", "with"
    };

    public static IReadOnlyList<MemorySearchMatch> Apply(
        string query,
        IReadOnlyList<MemorySearchMatch> matches)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return matches;
        }

        return matches
            .Where(match => Tokenize(match.Text).Overlaps(queryTokens))
            .ToArray();
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in SplitTokens(value))
        {
            if (token.Length >= 3 && !StopWords.Contains(token))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static IEnumerable<string> SplitTokens(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                continue;
            }

            if (length > 0)
            {
                yield return new string(buffer, 0, length);
                length = 0;
            }
        }

        if (length > 0)
        {
            yield return new string(buffer, 0, length);
        }
    }
}
