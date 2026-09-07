namespace IncidentCompass.Infrastructure.Tickets;

internal sealed record GitHubIssueParseResult(
    bool IsMalformed,
    IReadOnlyList<GitHubIssueCandidate> Candidates)
{
    public static GitHubIssueParseResult Malformed() => new(true, []);
}
