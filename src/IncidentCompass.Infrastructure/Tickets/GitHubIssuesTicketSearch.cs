using System.Net;
using System.Net.Http.Headers;
using IncidentCompass.Application.Tickets;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Tickets;

internal sealed class GitHubIssuesTicketSearch(
    HttpClient httpClient,
    IOptions<GitHubIssuesOptions> options) : ITicketSearch
{
    internal static readonly Uri Authority = new("https://api.github.com");

    public async Task<TicketSearchResult> SearchAsync(
        TicketSearchRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return TicketSearchResult.Unavailable("ticket_search_repository_unavailable");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            var repository = settings.ConfiguredRepository!;
            var query = GitHubIssueQueryBuilder.Build(repository, request);
            if (query is null)
            {
                return TicketSearchResult.NoMatch(
                    "github", repository, "ticket_search_no_safe_terms");
            }

            using var message = CreateRequest(query, settings.Token!);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var failure = MapFailure(response);
            if (failure is not null)
            {
                return TicketSearchResult.Unavailable(failure);
            }

            var candidates = await GitHubIssueResponseParser.ParseAsync(
                response.Content,
                settings.Owner!,
                settings.Repository!,
                timeout.Token);
            if (candidates.IsMalformed)
            {
                return TicketSearchResult.Unavailable("ticket_search_malformed_response");
            }

            var matches = GitHubIssueRanker.Rank(repository, request, candidates.Candidates);
            return matches.Count == 0
                ? TicketSearchResult.NoMatch("github", repository)
                : new TicketSearchResult(
                    TicketSearchOutcome.Matched,
                    "ticket_search_matches",
                    matches,
                    "github",
                    repository);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TicketSearchResult.Unavailable("ticket_search_timeout");
        }
        catch (HttpRequestException)
        {
            return TicketSearchResult.Unavailable("ticket_search_upstream_unavailable");
        }
        catch (IOException)
        {
            return TicketSearchResult.Unavailable("ticket_search_malformed_response");
        }
        catch (ArgumentException)
        {
            return TicketSearchResult.Unavailable("ticket_search_malformed_response");
        }
    }

    private static HttpRequestMessage CreateRequest(string query, string token)
    {
        var uri = new Uri(Authority, $"/search/issues?q={Uri.EscapeDataString(query)}&per_page=50");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("IncidentCompass/0.3");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static string? MapFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "ticket_search_authentication_failed",
            HttpStatusCode.Forbidden when IsRateLimited(response) => "ticket_search_rate_limited",
            HttpStatusCode.Forbidden => "ticket_search_forbidden",
            HttpStatusCode.TooManyRequests => "ticket_search_rate_limited",
            HttpStatusCode.NotFound => "ticket_search_repository_not_found",
            HttpStatusCode.UnprocessableEntity => "ticket_search_query_invalid",
            >= HttpStatusCode.InternalServerError => "ticket_search_upstream_unavailable",
            _ => "ticket_search_upstream_rejected"
        };
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.RetryAfter is not null ||
        (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.Contains("0", StringComparer.Ordinal));
}
