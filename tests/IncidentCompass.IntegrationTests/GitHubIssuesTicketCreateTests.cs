using System.Net;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class GitHubIssuesTicketCreateTests
{
    [Fact]
    public async Task EmptyPreflightMakesExactlyOneCanonicalCreateRequest()
    {
        const string token = "github-ticket-create-token-sentinel";
        var handler = new RecordingHandler((request, call) => call switch
        {
            1 => Json(HttpStatusCode.OK, new { items = Array.Empty<object>() }),
            2 => Json(HttpStatusCode.Created, Issue(42, "body")),
            _ => throw new InvalidOperationException("Unexpected provider call.")
        });
        using var tool = CreateTool(handler, token: token);
        var payload = Payload();

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), payload.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal([HttpMethod.Get, HttpMethod.Post], handler.Methods);
        Assert.Equal(1, handler.Methods.Count(method => method == HttpMethod.Post));
        Assert.Equal("Bearer", handler.AuthorizationSchemes[0]);
        Assert.All(handler.AuthorizationParameters, value => Assert.Equal(token, value));
        using var posted = JsonDocument.Parse(handler.RequestBodies[1]!);
        Assert.Equal(
            JsonDocument.Parse(payload.CanonicalPayload).RootElement.GetProperty("title").GetString(),
            posted.RootElement.GetProperty("title").GetString());
        Assert.Contains("incidentcompass-ticket", posted.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(result.CanonicalResult), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "github_issue_preflight_authentication_failed")]
    [InlineData(HttpStatusCode.Forbidden, "github_issue_preflight_forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "github_issue_preflight_rate_limited")]
    [InlineData(HttpStatusCode.NotFound, "github_issue_preflight_repository_not_found")]
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
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.FailureCode);
        Assert.DoesNotContain(handler.Methods, method => method == HttpMethod.Post);
        Assert.DoesNotContain("raw-secret", Encoding.UTF8.GetString(result.CanonicalResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingMarkerReturnsSafeIdentityWithoutPost()
    {
        var payload = Payload();
        using var payloadDocument = JsonDocument.Parse(payload.CanonicalPayload);
        var marker = payloadDocument.RootElement.GetProperty("marker").GetString()!;
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, new
        {
            items = new[] { Issue(73, $"before <!-- incidentcompass-ticket:{marker} --> after") }
        }));
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), payload.CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Methods));
        Assert.Contains("73", Encoding.UTF8.GetString(result.CanonicalResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostTransportAmbiguityIsUnknownAndNeverRetried()
    {
        var handler = new RecordingHandler((request, call) =>
        {
            if (call == 1)
            {
                return Json(HttpStatusCode.OK, new { items = Array.Empty<object>() });
            }

            throw new HttpRequestException("provider route sentinel");
        });
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("dispatch_outcome_unknown", result.FailureCode);
        Assert.Equal(1, handler.Methods.Count(method => method == HttpMethod.Post));
        Assert.Equal(2, handler.Methods.Count);
    }

    [Fact]
    public async Task PreflightCancellationAndTimeoutAreDefinitiveAndNeverPost()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledHandler = new BlockingHandler(HttpMethod.Get);
        using var cancelledTool = CreateTool(cancelledHandler);
        var cancelledExecution = cancelledTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, cancellation.Token);
        await cancelledHandler.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var cancelled = await cancelledExecution.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_preflight_timeout", cancelled.FailureCode);
        Assert.DoesNotContain(cancelledHandler.Methods, method => method == HttpMethod.Post);

        var timeoutHandler = new BlockingHandler(HttpMethod.Get);
        using var timeoutTool = CreateTool(timeoutHandler, timeoutSeconds: 1);
        var timedOut = await timeoutTool.ExecuteAsync(
                Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_preflight_timeout", timedOut.FailureCode);
        Assert.DoesNotContain(timeoutHandler.Methods, method => method == HttpMethod.Post);
    }

    [Fact]
    public async Task PostStartCancellationAndTimeoutAreUnknownAndNeverRetryPost()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledHandler = new BlockingHandler(HttpMethod.Post);
        using var cancelledTool = CreateTool(cancelledHandler);
        var cancelledExecution = cancelledTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, cancellation.Token);
        await cancelledHandler.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var cancelled = await cancelledExecution.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", cancelled.FailureCode);
        Assert.Equal(1, cancelledHandler.Methods.Count(method => method == HttpMethod.Post));

        var timeoutHandler = new BlockingHandler(HttpMethod.Post);
        using var timeoutTool = CreateTool(timeoutHandler, timeoutSeconds: 1);
        var timedOut = await timeoutTool.ExecuteAsync(
                Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("dispatch_outcome_unknown", timedOut.FailureCode);
        Assert.Equal(1, timeoutHandler.Methods.Count(method => method == HttpMethod.Post));
    }

    [Fact]
    public async Task PriorConfirmedActionReturnsWithoutProviderCall()
    {
        var confirmed = Encoding.UTF8.GetBytes("{\"issueNumber\":\"91\",\"provider\":\"github\"}");
        var history = new StubHistory(new TicketActionHistorySnapshot(
            confirmed, "GitHub accepted the issue.", [], false, false, false));
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var tool = CreateTool(handler, history);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(handler.Methods);
        Assert.Equal(confirmed, result.CanonicalResult);
    }

    [Fact]
    public async Task PriorUnknownMarkerOnlyPerformsBoundedReadsAndNeverPosts()
    {
        var history = new StubHistory(new TicketActionHistorySnapshot(
            null, null, [new string('a', 64)], false, false, false));
        var handler = new RecordingHandler((_, _) =>
            Json(HttpStatusCode.OK, new { items = Array.Empty<object>() }));
        using var tool = CreateTool(handler, history);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("github_issue_prior_outcome_unknown", result.FailureCode);
        Assert.Equal(2, handler.Methods.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData(true, false, "github_issue_history_exceeded")]
    [InlineData(false, true, "github_issue_prior_action_pending")]
    public async Task UnsafeLocalHistoryFailsClosedWithoutProviderCalls(
        bool historyLimitExceeded,
        bool hasPendingAction,
        string expectedCode)
    {
        var history = new StubHistory(new TicketActionHistorySnapshot(
            null, null, [], hasPendingAction, historyLimitExceeded, false));
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var tool = CreateTool(handler, history);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.FailureCode);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task UnreadableUnknownOrConfirmedHistoryFailsClosedWithoutProviderCalls()
    {
        var unsafeHandler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("No HTTP expected."));
        using var unsafeTool = CreateTool(unsafeHandler, new StubHistory(
            new TicketActionHistorySnapshot(null, null, [], false, false, true)));
        var unsafeResult = await unsafeTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);
        Assert.Equal("github_issue_history_invalid", unsafeResult.FailureCode);
        Assert.Empty(unsafeHandler.Methods);

        var malformedHandler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("No HTTP expected."));
        using var malformedTool = CreateTool(malformedHandler, new StubHistory(
            new TicketActionHistorySnapshot(
                Encoding.UTF8.GetBytes("{\"unexpected\":true}"), null, [], false, false, false)));
        var malformedResult = await malformedTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);
        Assert.Equal("github_issue_prior_result_invalid", malformedResult.FailureCode);
        Assert.Empty(malformedHandler.Methods);
    }

    [Fact]
    public async Task PayloadCannotInjectRepositoryOrProviderTarget()
    {
        var prepared = Payload();
        using var document = JsonDocument.Parse(prepared.CanonicalPayload);
        var root = document.RootElement;
        var injected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            body = root.GetProperty("body").GetString(),
            marker = root.GetProperty("marker").GetString(),
            originReportId = root.GetProperty("originReportId").GetString(),
            repository = "attacker/redirect",
            schemaVersion = 1,
            title = root.GetProperty("title").GetString()
        }));
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No HTTP expected."));
        using var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), injected, TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_payload_invalid", result.FailureCode);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task MalformedPreflightIsDefinitiveButMalformedPostResponseIsUnknown()
    {
        var preflightHandler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        using var preflightTool = CreateTool(preflightHandler);
        var preflight = await preflightTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);
        Assert.Equal("github_issue_preflight_malformed", preflight.FailureCode);
        Assert.DoesNotContain(preflightHandler.Methods, method => method == HttpMethod.Post);

        var postHandler = new RecordingHandler((_, call) => call == 1
            ? Json(HttpStatusCode.OK, new { items = Array.Empty<object>() })
            : new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            });
        using var postTool = CreateTool(postHandler);
        var post = await postTool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);
        Assert.Equal("dispatch_outcome_unknown", post.FailureCode);
        Assert.Equal(1, postHandler.Methods.Count(method => method == HttpMethod.Post));
    }

    [Fact]
    public async Task FailureLogsContainOnlyStableCodeAndNoCredentialOrProviderBody()
    {
        const string token = "github-log-token-sentinel";
        const string providerBody = "github-provider-body-sentinel";
        var messages = new List<string>();
        var handler = new RecordingHandler((_, call) => call == 1
            ? Json(HttpStatusCode.OK, new { items = Array.Empty<object>() })
            : new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(providerBody)
            });
        using var tool = CreateTool(handler, token: token, logger: new CapturingLogger(messages));

        var result = await tool.ExecuteAsync(
            Guid.NewGuid(), Payload().CanonicalPayload, TestContext.Current.CancellationToken);

        Assert.Equal("github_issue_request_invalid", result.FailureCode);
        Assert.Contains(messages, message =>
            message.Contains("github_issue_request_invalid", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message =>
            message.Contains(token, StringComparison.Ordinal) ||
            message.Contains(providerBody, StringComparison.Ordinal));
    }

    private static GitHubIssuesTicketCreate CreateTool(
        HttpMessageHandler handler,
        ITicketActionHistory? history = null,
        string token = "test-token",
        ILogger<GitHubIssuesTicketCreate>? logger = null,
        int timeoutSeconds = 10) =>
        new(
            Options.Create(new GitHubIssuesOptions
            {
                Owner = "owner",
                Repository = "repo",
                Token = token,
                TimeoutSeconds = timeoutSeconds
            }),
            history ?? new StubHistory(EmptyHistory()),
            handler,
            logger);

    private static ExternalActionPreparation Payload()
    {
        var reportId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        return TicketCreatePayloadFactory.Create(
            reportId, $"post-report:v1:{reportId:N}:ticket_create");
    }

    private static TicketActionHistorySnapshot EmptyHistory() =>
        new(null, null, [], false, false, false);

    private static object Issue(int number, string body) => new
    {
        number,
        body,
        html_url = $"https://github.com/owner/repo/issues/{number}"
    };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class StubHistory(TicketActionHistorySnapshot snapshot) : ITicketActionHistory
    {
        public Task<TicketActionHistorySnapshot> ReadPriorAsync(
            Guid actionId,
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<string?> RequestBodies { get; } = [];
        public List<string?> AuthorizationSchemes { get; } = [];
        public List<string?> AuthorizationParameters { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return response(request, Methods.Count);
        }
    }

    private sealed class BlockingHandler(HttpMethod blockedMethod) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

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

            return Json(HttpStatusCode.OK, new { items = Array.Empty<object>() });
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger<GitHubIssuesTicketCreate>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
    }
}
