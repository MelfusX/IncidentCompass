namespace IncidentCompass.Infrastructure.Tickets;

internal sealed record GitHubIssueCandidate(
    int Number,
    string Title,
    string Body,
    string Status,
    string? Assignee,
    DateTimeOffset CreatedAtUtc,
    string Url,
    IReadOnlyList<string> Labels);
