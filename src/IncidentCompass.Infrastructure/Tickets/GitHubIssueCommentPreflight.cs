using System.Net;
using System.Text.Json;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssueCommentPreflight
{
    private const int MaximumResponseBytes = 128 * 1024;

    public static async Task<(ExternalActionExecutionResult? Existing, string? FailureCode)> SafeCheckAsync(
        HttpClient client,
        GitHubIssuesOptions options,
        int issueNumber,
        string marker,
        CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var targetFailure = await ValidateTargetAsync(
                client, options, issueNumber, deadline.Token);
            return targetFailure is null
                ? await FindMarkerAsync(client, options, issueNumber, marker, deadline.Token)
                : (null, targetFailure);
        }
        catch (OperationCanceledException)
        {
            return (null, "github_issue_comment_preflight_timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return (null, "github_issue_comment_preflight_unavailable");
        }
    }

    private static async Task<string?> ValidateTargetAsync(
        HttpClient client,
        GitHubIssuesOptions options,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/repos/{options.Owner}/{options.Repository}/issues/{issueNumber}");
        GitHubIssueCreateRequestFactory.AddGitHubHeaders(request, options.Token!);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var failure = MapFailure(response.StatusCode);
        if (failure is not null)
        {
            return failure;
        }

        var bytes = await BoundedHttpContentReader.ReadAsync(
            response.Content, MaximumResponseBytes, cancellationToken);
        if (bytes is null)
        {
            return "github_issue_comment_preflight_malformed";
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                !root.TryGetProperty("pull_request", out _) &&
                root.TryGetProperty("number", out var number) &&
                number.TryGetInt32(out var actualNumber) && actualNumber == issueNumber &&
                root.TryGetProperty("html_url", out var url) && url.ValueKind == JsonValueKind.String &&
                string.Equals(url.GetString(),
                    $"https://github.com/{options.Owner}/{options.Repository}/issues/{issueNumber}",
                    StringComparison.Ordinal)
                    ? null
                    : "github_issue_comment_preflight_malformed";
        }
        catch (JsonException)
        {
            return "github_issue_comment_preflight_malformed";
        }
    }

    private static async Task<(ExternalActionExecutionResult? Existing, string? FailureCode)> FindMarkerAsync(
        HttpClient client,
        GitHubIssuesOptions options,
        int issueNumber,
        string marker,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/repos/{options.Owner}/{options.Repository}/issues/{issueNumber}/comments?per_page=100&page=1");
        GitHubIssueCreateRequestFactory.AddGitHubHeaders(request, options.Token!);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var failure = MapFailure(response.StatusCode);
        if (failure is not null)
        {
            return (null, failure);
        }

        if (response.Headers.TryGetValues("Link", out var links) &&
            links.Any(static value => value.Contains("rel=\"next\"", StringComparison.Ordinal)))
        {
            return (null, "github_issue_comment_history_exceeded");
        }

        var bytes = await BoundedHttpContentReader.ReadAsync(
            response.Content, MaximumResponseBytes, cancellationToken);
        if (bytes is null)
        {
            return (null, "github_issue_comment_preflight_malformed");
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() > 100)
            {
                return (null, "github_issue_comment_preflight_malformed");
            }

            ExternalActionExecutionResult? match = null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!HasMarker(item, marker))
                {
                    continue;
                }

                if (match is not null ||
                    !GitHubIssueCommentResponseParser.TryReadIdentity(
                        item, options, issueNumber, marker, out var commentId))
                {
                    return (null, "github_issue_comment_preflight_malformed");
                }

                match = GitHubIssueCommentResponseParser.Success(issueNumber, commentId);
            }

            return (match, null);
        }
        catch (JsonException)
        {
            return (null, "github_issue_comment_preflight_malformed");
        }
    }

    private static bool HasMarker(JsonElement item, string marker) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String &&
        body.GetString()!.Contains(GitHubIssueCommentMarker.Comment(marker), StringComparison.Ordinal);

    private static string? MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => null,
        HttpStatusCode.Unauthorized => "github_issue_comment_preflight_authentication_failed",
        HttpStatusCode.Forbidden => "github_issue_comment_preflight_forbidden",
        HttpStatusCode.TooManyRequests => "github_issue_comment_preflight_rate_limited",
        HttpStatusCode.NotFound => "github_issue_comment_target_not_found",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
            "github_issue_comment_preflight_invalid",
        _ => "github_issue_comment_preflight_unavailable"
    };
}
