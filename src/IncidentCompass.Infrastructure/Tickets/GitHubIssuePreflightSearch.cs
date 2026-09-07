using System.Net;
using System.Text.Json;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssuePreflightSearch
{
    private const int MaximumResponseBytes = 128 * 1024;

    public static async Task<GitHubIssuePreflightResult> SafeSearchAsync(
        HttpClient client,
        GitHubIssuesOptions options,
        string marker,
        CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            return await SearchAsync(client, options, marker, deadline.Token);
        }
        catch (OperationCanceledException)
        {
            return GitHubIssuePreflightResult.Failed("github_issue_preflight_timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return GitHubIssuePreflightResult.Failed("github_issue_preflight_unavailable");
        }
    }

    private static async Task<GitHubIssuePreflightResult> SearchAsync(
        HttpClient client,
        GitHubIssuesOptions options,
        string marker,
        CancellationToken cancellationToken)
    {
        var query = $"repo:{options.ConfiguredRepository} is:issue \"{GitHubIssueMarker.Comment(marker)}\"";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/search/issues?q={Uri.EscapeDataString(query)}&per_page=2");
        GitHubIssueCreateRequestFactory.AddGitHubHeaders(request, options.Token!);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var failure = MapFailure(response.StatusCode);
        if (failure is not null)
        {
            return GitHubIssuePreflightResult.Failed(failure);
        }

        var bytes = await BoundedHttpContentReader.ReadAsync(
            response.Content, MaximumResponseBytes, cancellationToken);
        if (bytes is null)
        {
            return GitHubIssuePreflightResult.Failed("github_issue_preflight_malformed");
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return GitHubIssuePreflightResult.Failed("github_issue_preflight_malformed");
            }

            var matches = new List<int>();
            foreach (var item in items.EnumerateArray().Take(2))
            {
                if (TryReadMatch(item, options, marker, out var issueNumber))
                {
                    matches.Add(issueNumber);
                }
            }

            return matches.Count == 0
                ? GitHubIssuePreflightResult.Empty()
                : GitHubIssuePreflightResult.Found(GitHubIssueCreateResponseParser.Success(matches.Min()));
        }
        catch (JsonException)
        {
            return GitHubIssuePreflightResult.Failed("github_issue_preflight_malformed");
        }
    }

    private static bool TryReadMatch(
        JsonElement item,
        GitHubIssuesOptions options,
        string marker,
        out int issueNumber)
    {
        issueNumber = 0;
        return item.ValueKind == JsonValueKind.Object &&
            !item.TryGetProperty("pull_request", out _) &&
            item.TryGetProperty("number", out var number) &&
            number.TryGetInt32(out issueNumber) && issueNumber > 0 &&
            item.TryGetProperty("html_url", out var url) && url.ValueKind == JsonValueKind.String &&
            string.Equals(url.GetString(),
                $"https://github.com/{options.Owner}/{options.Repository}/issues/{issueNumber}",
                StringComparison.Ordinal) &&
            item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String &&
            body.GetString()!.Contains(GitHubIssueMarker.Comment(marker), StringComparison.Ordinal);
    }

    private static string? MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => null,
        HttpStatusCode.Unauthorized => "github_issue_preflight_authentication_failed",
        HttpStatusCode.Forbidden => "github_issue_preflight_forbidden",
        HttpStatusCode.TooManyRequests => "github_issue_preflight_rate_limited",
        HttpStatusCode.NotFound => "github_issue_preflight_repository_not_found",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "github_issue_preflight_invalid",
        _ => "github_issue_preflight_unavailable"
    };

}
