using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class GitHubIssueQueryBuilderTests
{
    [Fact]
    public void Build_UsesFixedOrderAndBoundsHostileTerms()
    {
        var request = new TicketSearchRequest(
            new string('f', 128),
            "checkout repo:evil",
            "payments) OR is:pr",
            "TimeoutException",
            string.Join(' ', Enumerable.Range(0, 20).Select(index => "token" + index)),
            []);

        var query = GitHubIssueQueryBuilder.Build("owner/repository", request);

        Assert.NotNull(query);
        Assert.True(query.Length <= 256);
        Assert.StartsWith("repo:owner/repository is:issue (", query, StringComparison.Ordinal);
        Assert.True(Count(query, " OR ") <= GitHubIssueQueryBuilder.MaxOrOperators);
        var group = query[(query.IndexOf('(') + 1)..query.LastIndexOf(')')];
        Assert.DoesNotContain(':', group);
        Assert.DoesNotContain(')', group);
    }

    [Fact]
    public void Build_DropsTailThenTruncatesFirstOnlyAtCompleteToken()
    {
        var repository = new string('o', 39) + "/" + new string('r', 100);
        var request = new TicketSearchRequest(
            string.Join(' ', Enumerable.Repeat("abcdefghij", 12)),
            new string('s', 128), new string('c', 128), new string('e', 128), new string('m', 128), []);

        var query = GitHubIssueQueryBuilder.Build(repository, request);

        Assert.NotNull(query);
        Assert.True(query.Length <= 256);
        Assert.DoesNotContain(" OR ", query, StringComparison.Ordinal);
        var quoted = query[(query.IndexOf('"') + 1)..query.LastIndexOf('"')];
        Assert.False(quoted.EndsWith(' '));
        Assert.Contains(quoted, string.Join(' ', Enumerable.Repeat("abcdefghij", 12)), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmptySafeTermsSkipsRequest()
    {
        Assert.Null(GitHubIssueQueryBuilder.Build(
            "owner/repo",
            new TicketSearchRequest("!!!", "---", null, null, null, [])));
    }

    private static int Count(string value, string term) =>
        value.Split(term, StringSplitOptions.None).Length - 1;
}
