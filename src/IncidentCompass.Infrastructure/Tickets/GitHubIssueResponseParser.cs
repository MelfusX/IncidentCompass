using System.Globalization;
using System.Text.Json;
using IncidentCompass.Application.Tickets;

namespace IncidentCompass.Infrastructure.Tickets;

internal static class GitHubIssueResponseParser
{
    private const int MaxResponseBytes = 1024 * 1024;

    public static async Task<GitHubIssueParseResult> ParseAsync(
        HttpContent content,
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var bytes = await BoundedHttpContentReader.ReadAsync(content, MaxResponseBytes, cancellationToken);
        if (bytes is null)
        {
            return GitHubIssueParseResult.Malformed();
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return GitHubIssueParseResult.Malformed();
            }

            var candidates = new List<GitHubIssueCandidate>();
            foreach (var item in items.EnumerateArray().Take(50))
            {
                var candidate = ParseOne(item, owner, repository, out var malformed);
                if (malformed)
                {
                    return GitHubIssueParseResult.Malformed();
                }

                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            return new GitHubIssueParseResult(false, candidates);
        }
        catch (JsonException)
        {
            return GitHubIssueParseResult.Malformed();
        }
    }

    private static GitHubIssueCandidate? ParseOne(
        JsonElement item,
        string owner,
        string repository,
        out bool malformed)
    {
        malformed = false;
        if (item.ValueKind != JsonValueKind.Object)
        {
            malformed = true;
            return null;
        }

        if (item.TryGetProperty("pull_request", out _))
        {
            return null;
        }

        if (!TryReadPositiveInt(item, "number", out var number) ||
            !TryReadRequiredString(item, "repository_url", out var repositoryUrl) ||
            !TryReadRequiredString(item, "html_url", out var htmlUrl))
        {
            malformed = true;
            return null;
        }

        var expectedApiRepository = $"https://api.github.com/repos/{owner}/{repository}";
        var expectedUrl = $"https://github.com/{owner}/{repository}/issues/{number}";
        if (!string.Equals(repositoryUrl, expectedApiRepository, StringComparison.Ordinal) ||
            !string.Equals(htmlUrl, expectedUrl, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryReadRequiredString(item, "title", out var rawTitle) ||
            !TryReadRequiredString(item, "state", out var rawStatus) ||
            !TryReadRequiredString(item, "created_at", out var rawCreated) ||
            !TryReadOptionalString(item, "body", out var rawBody) ||
            !TryReadAssignee(item, out var assignee) ||
            !TryReadLabels(item, out var labels) ||
            !DateTimeOffset.TryParse(rawCreated, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created))
        {
            malformed = true;
            return null;
        }

        return new GitHubIssueCandidate(
            number,
            TicketTextBounds.Bound(rawTitle, 512, trim: false),
            rawBody is null ? string.Empty : TicketTextBounds.Bound(rawBody, 64 * 1024, trim: false),
            TicketTextBounds.Bound(rawStatus, 64, trim: false),
            assignee is null ? null : TicketTextBounds.Bound(assignee, 128, trim: false),
            created,
            expectedUrl,
            labels);
    }

    private static bool TryReadLabels(JsonElement item, out IReadOnlyList<string> values)
    {
        if (!item.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            values = [];
            return false;
        }

        var bounded = new List<string>();
        foreach (var label in labels.EnumerateArray().Take(50))
        {
            if (!TryReadRequiredString(label, "name", out var name))
            {
                values = [];
                return false;
            }

            bounded.Add(TicketTextBounds.Bound(name, 64, trim: false));
        }

        values = bounded;
        return true;
    }

    private static bool TryReadAssignee(JsonElement item, out string? value)
    {
        value = null;
        if (!item.TryGetProperty("assignee", out var assignee))
        {
            return false;
        }

        if (assignee.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryReadRequiredString(assignee, "login", out var login))
        {
            return false;
        }

        value = login;
        return true;
    }

    private static bool TryReadRequiredString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = element.GetString()!);
    }

    private static bool TryReadOptionalString(JsonElement item, string name, out string? value)
    {
        value = null;
        return item.TryGetProperty(name, out var element) &&
            (element.ValueKind == JsonValueKind.Null ||
             (element.ValueKind == JsonValueKind.String && (value = element.GetString()) is not null));
    }

    private static bool TryReadPositiveInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.TryGetProperty(name, out var element) && element.TryGetInt32(out value) && value > 0;
    }

}
