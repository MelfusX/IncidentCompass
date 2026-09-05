using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Infrastructure.Tickets;

internal sealed record GitHubIssuePreflightResult(
    bool IsEmpty,
    ExternalActionExecutionResult? Existing,
    string? FailureCode)
{
    public static GitHubIssuePreflightResult Empty() => new(true, null, null);

    public static GitHubIssuePreflightResult Found(ExternalActionExecutionResult existing) =>
        new(false, existing, null);

    public static GitHubIssuePreflightResult Failed(string code) => new(false, null, code);
}
