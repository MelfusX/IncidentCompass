using IncidentCompass.Application.Tickets;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssueQueryBuilder
{
    internal const int MaxQueryCharacters = 256;
    internal const int MaxOrOperators = 4;
    private const int MaxTermCharacters = 128;

    public static string? Build(string repository, TicketSearchRequest request)
    {
        var messagePhrase = string.Join(' ', TicketTextNormalizer.DistinctTokens(request.ErrorMessage, 6));
        var terms = new[]
            {
                request.Fingerprint,
                request.ErrorType,
                request.ServiceName,
                request.Component,
                messagePhrase
            }
            .Select(BoundTerm)
            .Where(static term => term.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (terms.Count == 0)
        {
            return null;
        }

        while (terms.Count > 1 && Compose(repository, terms).Length > MaxQueryCharacters)
        {
            terms.RemoveAt(terms.Count - 1);
        }

        if (Compose(repository, terms).Length > MaxQueryCharacters)
        {
            var fixedLength = Compose(repository, [string.Empty]).Length;
            terms[0] = TruncateToCompleteTokens(terms[0], MaxQueryCharacters - fixedLength);
        }

        return terms[0].Length == 0 ? null : Compose(repository, terms);
    }

    private static string Compose(string repository, IReadOnlyList<string> terms) =>
        $"repo:{repository} is:issue ({string.Join(" OR ", terms.Select(static term => $"\"{term}\""))})";

    private static string BoundTerm(string? value)
    {
        var normalized = TicketTextNormalizer.NormalizePhrase(value);
        return normalized.Length <= MaxTermCharacters ? normalized : normalized[..MaxTermCharacters].TrimEnd();
    }

    private static string TruncateToCompleteTokens(string value, int maximum)
    {
        if (maximum <= 0 || value.Length == 0)
        {
            return string.Empty;
        }

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var accepted = new List<string>();
        var length = 0;
        foreach (var token in tokens)
        {
            var candidateLength = length + (accepted.Count == 0 ? 0 : 1) + token.Length;
            if (candidateLength > maximum)
            {
                break;
            }

            accepted.Add(token);
            length = candidateLength;
        }

        return string.Join(' ', accepted);
    }
}
