using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Infrastructure.Tickets;

public static class GitHubIssueCreateResponseParser
{
    private const int MaximumResponseBytes = 64 * 1024;

    public static async Task<ExternalActionExecutionResult> ParseCreateAsync(
        HttpResponseMessage response,
        GitHubIssuesOptions options,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.Created)
        {
            return Failure(MapCreateStatus(response.StatusCode));
        }

        var identity = await ParseIdentityAsync(response.Content, options, cancellationToken);
        return identity is null
            ? Failure("dispatch_outcome_unknown")
            : Success(identity.Value);
    }

    public static bool TryValidateCanonicalResult(
        byte[] payload,
        out byte[] canonical,
        out string normalizedIssueNumber)
    {
        canonical = [];
        normalizedIssueNumber = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("provider", out var provider) ||
                provider.ValueKind != JsonValueKind.String || provider.GetString() != "github" ||
                !root.TryGetProperty("issueNumber", out var number) ||
                number.ValueKind != JsonValueKind.String ||
                !int.TryParse(
                    number.GetString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedIssueNumber) ||
                parsedIssueNumber <= 0)
            {
                return false;
            }

            canonical = Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(payload)));
            normalizedIssueNumber = parsedIssueNumber.ToString(CultureInfo.InvariantCulture);
            return string.Equals(number.GetString(), normalizedIssueNumber, StringComparison.Ordinal) &&
                   payload.AsSpan().SequenceEqual(canonical);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static async Task<int?> ParseIdentityAsync(
        HttpContent content,
        GitHubIssuesOptions options,
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
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("number", out var number) ||
                !number.TryGetInt32(out var issueNumber) || issueNumber <= 0 ||
                !root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String ||
                !string.Equals(url.GetString(),
                    $"https://github.com/{options.Owner}/{options.Repository}/issues/{issueNumber}",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return issueNumber;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static ExternalActionExecutionResult Success(int issueNumber)
    {
        var normalizedIssueNumber = issueNumber.ToString(CultureInfo.InvariantCulture);
        var result = new JsonObject
        {
            ["issueNumber"] = normalizedIssueNumber,
            ["provider"] = "github"
        };
        return new ExternalActionExecutionResult(
            true,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(result)),
            "GitHub accepted the issue.",
            AuditProjection: ExternalActionAuditProjection.GitHubIssueCreated(normalizedIssueNumber));
    }

    internal static ExternalActionExecutionResult Failure(string code)
    {
        var result = new JsonObject { ["code"] = code, ["provider"] = "github" };
        return new ExternalActionExecutionResult(
            false,
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(result)),
            "GitHub issue creation was not confirmed.",
            code);
    }

    private static string MapCreateStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "github_issue_authentication_failed",
        HttpStatusCode.Forbidden => "github_issue_forbidden",
        HttpStatusCode.TooManyRequests => "github_issue_rate_limited",
        HttpStatusCode.NotFound => "github_issue_repository_not_found",
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "github_issue_request_invalid",
        _ when (int)statusCode >= 500 => "dispatch_outcome_unknown",
        _ => "github_issue_create_rejected"
    };
}
