using IncidentCompass.Application.Tickets;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssueRanker
{
    private const double MinimumScore = 0.15;

    public static IReadOnlyList<TicketSearchMatch> Rank(
        string repository,
        TicketSearchRequest request,
        IReadOnlyList<GitHubIssueCandidate> candidates) =>
        candidates
            .Select(candidate => (Candidate: candidate, Score: Score(request, candidate)))
            .Where(static item => item.Score >= MinimumScore)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Candidate.Number)
            .Take(5)
            .Select(item => new TicketSearchMatch(
                "github",
                repository,
                item.Candidate.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Candidate.Title,
                item.Candidate.Status,
                item.Candidate.Assignee,
                item.Candidate.CreatedAtUtc,
                item.Candidate.Url,
                item.Score))
            .ToArray();

    private static double Score(TicketSearchRequest request, GitHubIssueCandidate candidate)
    {
        var text = candidate.Title + " " + candidate.Body;
        var labels = candidate.Labels
            .Select(TicketTextNormalizer.NormalizePhrase)
            .Where(static label => label.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var fingerprint = Exact(request.Fingerprint, text, labels);
        var service = Exact(request.ServiceName, text, labels);
        var component = Exact(request.Component, text, labels);
        var errorType = Exact(request.ErrorType, text, labels);
        var labelOverlap = LabelOverlap(request.KnownLabels, labels);
        var jaccard = MessageJaccard(request.ErrorMessage, text + " " + string.Join(' ', candidate.Labels));
        return Math.Round(
            0.40 * fingerprint + 0.15 * service + 0.15 * component +
            0.15 * errorType + 0.10 * labelOverlap + 0.05 * jaccard,
            6,
            MidpointRounding.AwayFromZero);
    }

    private static int Exact(string? phrase, string text, IReadOnlySet<string> labels)
    {
        var normalized = TicketTextNormalizer.NormalizePhrase(phrase);
        return normalized.Length > 0 &&
            (TicketTextNormalizer.ContainsWholePhrase(text, normalized) || labels.Contains(normalized))
                ? 1
                : 0;
    }

    private static double LabelOverlap(IReadOnlyList<string> incidentLabels, IReadOnlySet<string> candidateLabels)
    {
        var normalized = incidentLabels.Select(TicketTextNormalizer.NormalizePhrase)
            .Where(static label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length == 0
            ? 0
            : normalized.Count(candidateLabels.Contains) / (double)normalized.Length;
    }

    private static double MessageJaccard(string? message, string candidateText)
    {
        var incident = TicketTextNormalizer.DistinctTokens(message, 32).ToHashSet(StringComparer.Ordinal);
        var candidate = TicketTextNormalizer.DistinctTokens(candidateText, 256).ToHashSet(StringComparer.Ordinal);
        if (incident.Count == 0 && candidate.Count == 0)
        {
            return 0;
        }

        var intersection = incident.Count(candidate.Contains);
        return intersection / (double)incident.Union(candidate).Count();
    }
}
