using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Infrastructure.Tickets;

public static class GitHubIssueCommentResponseParser
{
    private const int MaximumResponseBytes = 64 * 1024;

    public static async Task<ExternalActionExecutionResult> ParseCreateAsync(
        HttpResponseMessage response,
        GitHubIssuesOptions options,
        int issueNumber,
        string marker,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.Created)
        {
            return Failure(MapCreateStatus(response.StatusCode));
        }

        var identity = await ParseIdentityAsync(
            response.Content, options, issueNumber, marker, cancellationToken);
        return identity is null
            ? Failure("dispatch_outcome_unknown")
            : Success(issueNumber, identity.Value);
    }

    internal static async Task<long?> ParseIdentityAsync(
        HttpContent content,
        GitHubIssuesOptions options,
        int issueNumber,
        string marker,
        CancellationToken cancellationToken)
    {
        var bytes = await BoundedHttpContentReader.ReadAsync(
            content, MaximumResponseBytes, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return TryReadIdentity(document.RootElement, options, issueNumber, marker, out var commentId)
                ? commentId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool TryReadIdentity(
        JsonElement root,
        GitHubIssuesOptions options,
        int issueNumber,
        string marker,
        out long commentId)
    {
        commentId = 0;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("id", out var id) && id.TryGetInt64(out commentId) && commentId > 0 &&
            root.TryGetProperty("html_url", out var htmlUrl) && htmlUrl.ValueKind == JsonValueKind.String &&
            string.Equals(htmlUrl.GetString(),
                $"https://github.com/{options.Owner}/{options.Repository}/issues/{issueNumber}#issuecomment-{commentId}",
                StringComparison.Ordinal) &&
            root.TryGetProperty("issue_url", out var issueUrl) && issueUrl.ValueKind == JsonValueKind.String &&
            string.Equals(issueUrl.GetString(),
                $"https://api.github.com/repos/{options.Owner}/{options.Repository}/issues/{issueNumber}",
                StringComparison.Ordinal) &&
            root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String &&
            body.GetString()!.Contains(GitHubIssueCommentMarker.Comment(marker), StringComparison.Ordinal);
    }

    internal static ExternalActionExecutionResult Success(int issueNumber, long commentId)
    {
        var normalizedIssueNumber = issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = new JsonObject
        {
            ["commentId"] = commentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["issueNumber"] = normalizedIssueNumber,
            ["provider"] = "github"
        };
        return new ExternalActionExecutionResult(
            true,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(result)),
            "GitHub accepted the issue comment.",
            AuditProjection: ExternalActionAuditProjection.GitHubIssueCommentAdded(normalizedIssueNumber));
    }

    internal static ExternalActionExecutionResult Failure(string code)
    {
        var result = new JsonObject { ["code"] = code, ["provider"] = "github" };
        return new ExternalActionExecutionResult(
            false,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(result)),
            "GitHub issue comment was not confirmed.",
            code);
    }

    private static string MapCreateStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "github_issue_comment_authentication_failed",
        HttpStatusCode.Forbidden => "github_issue_comment_forbidden",
        HttpStatusCode.TooManyRequests => "github_issue_comment_rate_limited",
        HttpStatusCode.NotFound => "github_issue_comment_target_not_found",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
            "github_issue_comment_request_invalid",
        _ when (int)statusCode >= 500 => "dispatch_outcome_unknown",
        _ => "github_issue_comment_rejected"
    };
}
