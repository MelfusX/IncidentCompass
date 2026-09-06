using System.Net;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class GitHubIssuesTicketSearchTests
{
    [Fact]
    public async Task Search_SendsOneFixedAuthorityBoundedRequestAndReturnsRankedBodyFreeMatch()
    {
        const string token = "ticket-token-sentinel";
        const string bodySecret = "upstream-body-secret-sentinel";
        var handler = new StubHandler((request, _) => Task.FromResult(JsonResponse(new
        {
            items = new[] { Issue(42, "fault-fingerprint checkout TimeoutException", bodySecret) }
        })));
        var adapter = CreateAdapter(handler, token: token);

        var result = await adapter.SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.Matched, result.Outcome);
        Assert.Equal("github", result.Provider);
        Assert.Equal("owner/repo", result.Repository);
        var match = Assert.Single(result.Matches);
        Assert.Equal("42", match.ExternalId);
        Assert.Equal("https://github.com/owner/repo/issues/42", match.Url);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("api.github.com", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal("https", handler.LastRequest.RequestUri.Scheme);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(token, handler.LastRequest.Headers.Authorization.Parameter);
        var decodedQuery = Uri.UnescapeDataString(handler.LastRequest.RequestUri.Query);
        Assert.Contains("repo:owner/repo is:issue", decodedQuery, StringComparison.Ordinal);
        Assert.Contains("per_page=50", decodedQuery, StringComparison.Ordinal);
        Assert.True(ExtractQuery(decodedQuery).Length <= 256);
        Assert.DoesNotContain(bodySecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_RejectsPullRequestsCrossRepositoryAndInvalidCanonicalUrls()
    {
        var valid = Issue(1, "fault-fingerprint", "body");
        var pullRequest = Merge(valid, new { pull_request = new { url = "https://api.github.com/pulls/1" } });
        var crossRepository = Merge(Issue(2, "fault-fingerprint", "body"),
            new { repository_url = "https://api.github.com/repos/other/repo" });
        var invalidUrl = Merge(Issue(3, "fault-fingerprint", "body"),
            new { html_url = "https://example.test/owner/repo/issues/3" });
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            items = new[] { pullRequest, crossRepository, invalidUrl }
        })));

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.NoMatch, result.Outcome);
        Assert.Equal("github", result.Provider);
        Assert.Equal("owner/repo", result.Repository);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Search_EmptySafeTermsReturnsLocalNoMatchWithoutHttp()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var request = new TicketSearchRequest("!!!", "---", null, null, null, []);

        var result = await CreateAdapter(handler).SearchAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.NoMatch, result.Outcome);
        Assert.Equal("ticket_search_no_safe_terms", result.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Search_ConsidersAtMostFirstFiftyCandidates()
    {
        var items = Enumerable.Range(1, 50)
            .Select(number => Issue(number, "unrelated", "body"))
            .Append(Issue(51, "fault-fingerprint checkout TimeoutException", "body"))
            .ToArray();
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(new { items })));

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.NoMatch, result.Outcome);
    }

    [Fact]
    public async Task Search_RuneSafeBoundsPreserveValidGitHubResponseText()
    {
        const string prefix = "fault-fingerprint ";
        var title = prefix + new string('x', 511 - prefix.Length) + "😀";
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            items = new[] { Issue(42, title, "body") }
        })));

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        var match = Assert.Single(result.Matches);
        Assert.Equal(511, match.Title.Length);
        Assert.False(char.IsSurrogate(match.Title[^1]));
    }

    [Theory]
    [InlineData("HTTPS://github.com/owner/repo/issues/42")]
    [InlineData("https://GitHub.com/owner/repo/issues/42")]
    [InlineData("https://github.com/owner/repo/Issues/42")]
    [InlineData("https://github.com/Owner/repo/issues/42")]
    public async Task Search_RejectsNonCanonicalUrlCasing(string url)
    {
        var candidate = Merge(Issue(42, "fault-fingerprint", "body"), new { html_url = url });
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(new { items = new[] { candidate } })));

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.NoMatch, result.Outcome);
        Assert.Equal("ticket_search_no_matches", result.Code);
    }

    [Fact]
    public async Task Search_AnyMalformedIssueMakesTheWholeResponseUnavailable()
    {
        var valid = Issue(42, "fault-fingerprint", "body");
        var malformedCandidates = new[]
        {
            Remove(valid, "title"),
            Merge(valid, new { title = 42 }),
            Remove(valid, "state"),
            Merge(valid, new { created_at = "not-a-date" }),
            Merge(valid, new { number = 0 }),
            Merge(valid, new { labels = "sev1" }),
            Merge(valid, new { body = 42 }),
            Merge(valid, new { assignee = "octocat" }),
            Remove(valid, "repository_url")
        };

        foreach (var malformed in malformedCandidates)
        {
            var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(new
            {
                items = new object[] { valid, malformed }
            })));

            var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

            Assert.Equal(TicketSearchOutcome.ConnectorUnavailable, result.Outcome);
            Assert.Equal("ticket_search_malformed_response", result.Code);
            Assert.Empty(result.Matches);
            Assert.DoesNotContain("body", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Search_InvalidUnicodeIsSanitizedWithoutCallingHttp()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        var request = Request() with { Fingerprint = "invalid\uD83D" };

        var result = await CreateAdapter(handler).SearchAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.ConnectorUnavailable, result.Outcome);
        Assert.Equal("ticket_search_malformed_response", result.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"response\"")]
    [InlineData("42")]
    [InlineData("false")]
    public async Task Search_ValidNonObjectJsonRootIsSanitizedMalformed(string json)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.ConnectorUnavailable, result.Outcome);
        Assert.Equal("ticket_search_malformed_response", result.Code);
        Assert.Empty(result.Matches);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false, "ticket_search_authentication_failed")]
    [InlineData(HttpStatusCode.Forbidden, false, "ticket_search_forbidden")]
    [InlineData(HttpStatusCode.Forbidden, true, "ticket_search_rate_limited")]
    [InlineData(HttpStatusCode.TooManyRequests, false, "ticket_search_rate_limited")]
    [InlineData(HttpStatusCode.NotFound, false, "ticket_search_repository_not_found")]
    [InlineData(HttpStatusCode.UnprocessableEntity, false, "ticket_search_query_invalid")]
    [InlineData(HttpStatusCode.InternalServerError, false, "ticket_search_upstream_unavailable")]
    public async Task Search_MapsFailuresWithoutUpstreamBody(
        HttpStatusCode status,
        bool rateLimited,
        string expectedCode)
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent("upstream-secret-body")
            };
            if (rateLimited)
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
            }

            return Task.FromResult(response);
        });

        var result = await CreateAdapter(handler).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(TicketSearchOutcome.ConnectorUnavailable, result.Outcome);
        Assert.Equal(expectedCode, result.Code);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_SeparatesAdapterTimeoutFromCallerCancellation()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The bounded delay should be cancelled.");
        });
        var timedOut = await CreateAdapter(handler, timeoutSeconds: 1)
            .SearchAsync(Request(), CancellationToken.None);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateAdapter(handler).SearchAsync(Request(), callerCancellation.Token));
        Assert.Equal("ticket_search_timeout", timedOut.Code);
    }

    [Fact]
    public async Task Search_MalformedOrOversizedResponseIsSanitized()
    {
        var malformed = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json")
        }));
        var oversized = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[1024 * 1024 + 1])
        }));

        var malformedResult = await CreateAdapter(malformed).SearchAsync(Request(), TestContext.Current.CancellationToken);
        var oversizedResult = await CreateAdapter(oversized).SearchAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("ticket_search_malformed_response", malformedResult.Code);
        Assert.Equal("ticket_search_malformed_response", oversizedResult.Code);
    }

    private static GitHubIssuesTicketSearch CreateAdapter(
        HttpMessageHandler handler,
        string token = "test-token",
        int timeoutSeconds = 10) =>
        new(new HttpClient(handler), Options.Create(new GitHubIssuesOptions
        {
            Owner = "owner",
            Repository = "repo",
            Token = token,
            TimeoutSeconds = timeoutSeconds
        }));

    private static TicketSearchRequest Request() =>
        new("fault-fingerprint", "checkout", "payments", "TimeoutException", "payment timed out", ["sev1"]);

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static object Issue(int number, string title, string body) => new
    {
        number,
        title,
        body,
        state = "open",
        assignee = new { login = "octocat" },
        created_at = "2026-01-15T00:00:00Z",
        html_url = $"https://github.com/owner/repo/issues/{number}",
        repository_url = "https://api.github.com/repos/owner/repo",
        labels = new[] { new { name = "sev1" } }
    };

    private static Dictionary<string, object?> Merge(object original, object replacement)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(original))!;
        foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(replacement))!)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static Dictionary<string, object?> Remove(object original, string property)
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(original))!;
        result.Remove(property);
        return result;
    }

    private static string ExtractQuery(string decodedQuery)
    {
        var start = decodedQuery.IndexOf("?q=", StringComparison.Ordinal) + 3;
        var end = decodedQuery.IndexOf("&per_page", StringComparison.Ordinal);
        return decodedQuery[start..end];
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return response(request, cancellationToken);
        }
    }
}
