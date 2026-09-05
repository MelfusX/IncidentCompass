using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Governance.PostReportActions;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class GitHubIssueCommentExternalActionToolTests
{
    [Fact]
    public async Task EmptyPreflightMakesExactlyOneFrozenCommentRequest()
    {
        const string token = "github-comment-token-sentinel";
        ExternalActionPreparation? prepared = null;
        var handler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, Array.Empty<object>()),
            3 => Json(HttpStatusCode.Created, Comment(91, 42, Body(prepared!))),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var tool = CreateTool(handler, token);
        prepared = Payload(tool);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), prepared.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Post], handler.Methods);
        Assert.Equal(1, handler.Methods.Count(static method => method == HttpMethod.Post));
        Assert.Equal("/repos/owner/repo/issues/42/comments", handler.Paths[2]);
        using var posted = JsonDocument.Parse(handler.RequestBodies[2]!);
        Assert.Equal(Body(prepared), posted.RootElement.GetProperty("body").GetString());
        Assert.All(handler.Authorization, header =>
        {
            Assert.Equal("Bearer", header?.Scheme);
            Assert.Equal(token, header?.Parameter);
        });
        var safeResult = Encoding.UTF8.GetString(result.CanonicalResult);
        Assert.Contains("\"commentId\":\"91\"", safeResult, StringComparison.Ordinal);
        Assert.DoesNotContain(token, safeResult, StringComparison.Ordinal);
        Assert.DoesNotContain("owner/repo", safeResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingMarkerReturnsSafeIdentityWithoutPost()
    {
        ExternalActionPreparation? prepared = null;
        var handler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, new[] { Comment(73, 42, Body(prepared!)) }),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var tool = CreateTool(handler);
        prepared = Payload(tool);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), prepared.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal([HttpMethod.Get, HttpMethod.Get], handler.Methods);
        Assert.Contains("\"commentId\":\"73\"", Encoding.UTF8.GetString(result.CanonicalResult),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousOrPaginatedCommentHistoryFailsClosedWithoutPost()
    {
        ExternalActionPreparation? duplicatePayload = null;
        var duplicateHandler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, new[]
            {
                Comment(73, 42, Body(duplicatePayload!)),
                Comment(74, 42, Body(duplicatePayload!))
            }),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var duplicateTool = CreateTool(duplicateHandler);
        duplicatePayload = Payload(duplicateTool);

        var duplicateResult = await duplicateTool.ExecuteAsync(
            Guid.NewGuid(), duplicatePayload.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_comment_preflight_malformed", duplicateResult.FailureCode);
        Assert.DoesNotContain(duplicateHandler.Methods, static method => method == HttpMethod.Post);

        var paginatedHandler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => PaginatedComments(),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var paginatedTool = CreateTool(paginatedHandler);

        var paginatedResult = await paginatedTool.ExecuteAsync(
            Guid.NewGuid(), Payload(paginatedTool).CanonicalPayload,
            TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_comment_history_exceeded", paginatedResult.FailureCode);
        Assert.DoesNotContain(paginatedHandler.Methods, static method => method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "github_issue_comment_preflight_authentication_failed")]
    [InlineData(HttpStatusCode.Forbidden, "github_issue_comment_preflight_forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "github_issue_comment_preflight_rate_limited")]
    [InlineData(HttpStatusCode.NotFound, "github_issue_comment_target_not_found")]
    public async Task PreflightFailureIsDefinitiveAndMakesNoPost(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("provider-raw-secret")
        });
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload(tool).CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.FailureCode);
        Assert.DoesNotContain(handler.Methods, static method => method == HttpMethod.Post);
        Assert.DoesNotContain("raw-secret", Encoding.UTF8.GetString(result.CanonicalResult),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightCancellationIsDefinitiveAndNeverPosts()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new BlockingHandler(HttpMethod.Get);
        using var tool = CreateTool(handler);
        var execution = tool.ExecuteAsync(Guid.NewGuid(), Payload(tool).CanonicalPayload, cancellation.Token);
        await handler.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var result = await execution.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_comment_preflight_timeout", result.FailureCode);
        Assert.DoesNotContain(handler.Methods, static method => method == HttpMethod.Post);
    }

    [Fact]
    public async Task PostTransportAndTimeoutAreUnknownAndNeverRetried()
    {
        var throwing = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, Array.Empty<object>()),
            _ => throw new HttpRequestException("provider route sentinel")
        });
        using var throwingTool = CreateTool(throwing);

        var transportResult = await throwingTool.ExecuteAsync(
            Guid.NewGuid(), Payload(throwingTool).CanonicalPayload,
            TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", transportResult.FailureCode);
        Assert.Equal(1, throwing.Methods.Count(static method => method == HttpMethod.Post));

        var blocking = new BlockingHandler(HttpMethod.Post, Issue(42), Array.Empty<object>());
        using var timeoutTool = CreateTool(blocking, timeoutSeconds: 1);
        var timeoutResult = await timeoutTool.ExecuteAsync(
                Guid.NewGuid(), Payload(timeoutTool).CanonicalPayload,
                TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", timeoutResult.FailureCode);
        Assert.Equal(1, blocking.Methods.Count(static method => method == HttpMethod.Post));
    }

    [Fact]
    public async Task CallerCancellationAfterPostStartsIsUnknownAndNeverRetried()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new BlockingHandler(HttpMethod.Post, Issue(42), Array.Empty<object>());
        using var tool = CreateTool(handler);
        var execution = tool.ExecuteAsync(
            Guid.NewGuid(), Payload(tool).CanonicalPayload, cancellation.Token);
        await handler.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var result = await execution.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", result.FailureCode);
        Assert.Equal(1, handler.Methods.Count(static method => method == HttpMethod.Post));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "github_issue_comment_authentication_failed")]
    [InlineData(HttpStatusCode.Forbidden, "github_issue_comment_forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "github_issue_comment_rate_limited")]
    [InlineData(HttpStatusCode.NotFound, "github_issue_comment_target_not_found")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "github_issue_comment_request_invalid")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dispatch_outcome_unknown")]
    public async Task PostFailuresReturnStableSafeCodes(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var handler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, Array.Empty<object>()),
            3 => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("provider-body-secret-sentinel")
            },
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload(tool).CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, result.FailureCode);
        Assert.Equal(1, handler.Methods.Count(static method => method == HttpMethod.Post));
        Assert.DoesNotContain("secret-sentinel", Encoding.UTF8.GetString(result.CanonicalResult),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableCreatedResponseIsOutcomeUnknown()
    {
        var handler = new RecordingHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, Issue(42)),
            2 => Json(HttpStatusCode.OK, Array.Empty<object>()),
            3 => Json(HttpStatusCode.Created, new { id = 91, body = "provider-body-secret-sentinel" }),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload(tool).CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", result.FailureCode);
        Assert.Equal(1, handler.Methods.Count(static method => method == HttpMethod.Post));
        Assert.DoesNotContain("secret-sentinel", Encoding.UTF8.GetString(result.CanonicalResult),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentsCannotInjectTargetOrRepository()
    {
        using var tool = CreateTool(new RecordingHandler((_, _) =>
            throw new InvalidOperationException("No call expected.")));
        var reportId = Guid.NewGuid();
        var key = $"post-report:v1:{reportId:N}:{TicketUpdatePostReportActionWorkflow.UpdateToolId}";
        var result = tool.Validate(JsonSerializer.SerializeToElement(new
        {
            originReportId = reportId.ToString("N"),
            proposalKey = key,
            ticketId = "42",
            repository = "attacker/repo"
        }));

        Assert.False(result.IsValid);
    }

    private static GitHubIssueCommentExternalActionTool CreateTool(
        HttpMessageHandler handler,
        string token = "test-token",
        int timeoutSeconds = 10) => new(
        Options.Create(new GitHubIssuesOptions
        {
            Owner = "owner",
            Repository = "repo",
            Token = token,
            TimeoutSeconds = timeoutSeconds
        }),
        handler);

    private static ExternalActionPreparation Payload(GitHubIssueCommentExternalActionTool tool)
    {
        var reportId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var proposalKey =
            $"post-report:v1:{reportId:N}:{TicketUpdatePostReportActionWorkflow.UpdateToolId}";
        var validation = tool.Validate(JsonSerializer.SerializeToElement(new
        {
            originReportId = reportId.ToString("N"),
            proposalKey,
            ticketId = "42"
        }));
        Assert.True(validation.IsValid);
        return tool.Prepare(validation.SanitizedArguments);
    }

    private static string Body(ExternalActionPreparation payload)
    {
        using var document = JsonDocument.Parse(payload.CanonicalPayload);
        return document.RootElement.GetProperty("body").GetString()!;
    }

    private static object Issue(int number) => new
    {
        number,
        html_url = $"https://github.com/owner/repo/issues/{number}"
    };

    private static object Comment(long id, int issueNumber, string body) => new
    {
        id,
        html_url = $"https://github.com/owner/repo/issues/{issueNumber}#issuecomment-{id}",
        issue_url = $"https://api.github.com/repos/owner/repo/issues/{issueNumber}",
        body
    };

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage PaginatedComments()
    {
        var response = Json(HttpStatusCode.OK, Array.Empty<object>());
        response.Headers.TryAddWithoutValidation(
            "Link",
            "<https://api.github.com/repos/owner/repo/issues/42/comments?per_page=100&page=2>; rel=\"next\"");
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Paths { get; } = [];
        public List<byte[]?> RequestBodies { get; } = [];
        public List<AuthenticationHeaderValue?> Authorization { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Paths.Add(request.RequestUri!.PathAndQuery);
            Authorization.Add(request.Headers.Authorization);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return responseFactory(request, Methods.Count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingHandler(
        HttpMethod blockedMethod,
        object? firstResponse = null,
        object? secondResponse = null) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<HttpMethod> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            if (request.Method == blockedMethod)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var value = Methods.Count == 1 ? firstResponse : secondResponse;
            return Json(HttpStatusCode.OK, value ?? Array.Empty<object>());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
