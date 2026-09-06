using System.Globalization;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class GitHubIssueRankerTests
{
    [Fact]
    public void Rank_UsesFormKcWholeTokensLabelsFormulaThresholdLimitAndTieBreak()
    {
        var request = new TicketSearchRequest(
            "ABC-123", "checkout", "pay", "TimeoutException", "card timed out", ["sev1", "payments"]);
        var candidates = Enumerable.Range(1, 7)
            .Select(number => Candidate(number, "ＡＢＣ 123 checkout payment timeoutException", ["sev1", "payments"]))
            .Reverse()
            .Append(Candidate(99, "concatenate checkoutish timeoutExceptions", []))
            .ToArray();

        var matches = GitHubIssueRanker.Rank("owner/repo", request, candidates);

        Assert.Equal(5, matches.Count);
        Assert.Equal(["1", "2", "3", "4", "5"], matches.Select(match => match.ExternalId));
        Assert.All(matches, match => Assert.Equal(0.8, match.Score));
        Assert.DoesNotContain(matches, match => match.ExternalId == "99");
    }

    [Fact]
    public void Rank_ZeroAndBelowThresholdCandidatesAreNoMatch()
    {
        var request = new TicketSearchRequest("fingerprint", "checkout", "component", "exception", "message", []);
        var candidates = new[]
        {
            Candidate(1, "unrelated", []),
            Candidate(2, "message", [])
        };

        Assert.Empty(GitHubIssueRanker.Rank("owner/repo", request, candidates));
    }

    [Fact]
    public void Rank_AppliesExactFeatureWeightsAndSixDecimalRounding()
    {
        var request = new TicketSearchRequest("fp", "svc", "cmp", "err", "msgone msgtwo", ["label"]);
        var candidates = new[]
        {
            Candidate(1, "fp", []),
            Candidate(2, "svc", ["label"]),
            Candidate(3, "svc msgone msgtwo", []),
            Candidate(4, "svc", []),
            Candidate(5, "cmp", []),
            Candidate(6, "err", []),
            Candidate(7, "label", ["label"])
        };

        var matches = GitHubIssueRanker.Rank("owner/repo", request, candidates);

        Assert.Equal([0.4, 0.25, 0.183333, 0.15, 0.15], matches.Select(match => match.Score));
        Assert.Equal(["1", "2", "3", "4", "5"], matches.Select(match => match.ExternalId));
    }

    private static GitHubIssueCandidate Candidate(int number, string text, IReadOnlyList<string> labels) =>
        new(number, text, string.Empty, "open", null, DateTimeOffset.Parse("2026-01-15T00:00:00Z", CultureInfo.InvariantCulture),
            $"https://github.com/owner/repo/issues/{number}", labels);
}
