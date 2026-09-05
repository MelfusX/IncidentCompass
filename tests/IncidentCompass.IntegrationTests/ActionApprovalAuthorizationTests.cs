using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Application.Governance.ActionApprovals;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class ActionApprovalAuthorizationTests(PostgresRepositoryFixture postgres)
{
    private const string OperatorKey = "action_operator_key_abcdefghijklmnopqrstuvwxyz123456";
    private const string OtherOperatorKey = "other_action_operator_key_abcdefghijklmnop123456";

    [Fact]
    public async Task ApiKeyAuthDisabled_AllApprovalRoutesReturnForbiddenBeforeRepositoryAccess()
    {
        var repository = new CountingReviewRepository();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(static logging => logging.ClearProviders());
            builder.UseExplicitMockProviders();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IActionApprovalReviewRepository>();
                services.AddSingleton<IActionApprovalReviewRepository>(repository);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var id = Guid.NewGuid();
        var body = new { payloadSha256 = new string('a', 64), approvalSha256 = new string('b', 64) };

        var responses = new[]
        {
            await client.GetAsync("/api/v1/action-approvals", TestContext.Current.CancellationToken),
            await client.GetAsync($"/api/v1/action-approvals/{id}", TestContext.Current.CancellationToken),
            await client.PostAsJsonAsync($"/api/v1/action-approvals/{id}/approve", body, TestContext.Current.CancellationToken),
            await client.PostAsJsonAsync($"/api/v1/action-approvals/{id}/reject", body, TestContext.Current.CancellationToken)
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal(0, repository.AccessCount);
    }

    [Fact]
    public async Task ApiKeyAuthEnabled_UsesOnlyServerKeyTenantAndActor()
    {
        var repository = new CountingReviewRepository();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(static logging => logging.ClearProviders());
            builder.UseExplicitMockProviders();
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:PermitLimit", "100");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:WindowSeconds", "300");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Credentials:0:KeyId", "operator-a");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Credentials:0:TenantId", "tenant-a");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Credentials:0:Sha256Digest", Digest(OperatorKey));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IActionApprovalReviewRepository>();
                services.AddSingleton<IActionApprovalReviewRepository>(repository);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/action-approvals");
        list.Headers.Add("X-IncidentCompass-Key", OperatorKey);
        list.Headers.Add("X-Demo-Tenant-Id", "spoofed-tenant");

        var listResponse = await client.SendAsync(list, TestContext.Current.CancellationToken);
        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/action-approvals/{Guid.NewGuid()}/approve")
        {
            Content = JsonContent.Create(new
            {
                payloadSha256 = new string('a', 64),
                approvalSha256 = new string('b', 64),
                tenantId = "spoofed-tenant",
                actor = "spoofed-actor"
            })
        };
        approve.Headers.Add("X-IncidentCompass-Key", OperatorKey);
        var approveResponse = await client.SendAsync(approve, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approveResponse.StatusCode);
        Assert.Equal("tenant-a", repository.LastTenantId);
        Assert.Equal("key:operator-a", repository.LastDecision?.Actor);
    }

    [DockerAvailableFact]
    public async Task ApiKeyAuthEnabled_TwoTenantsCannotReadOrDecideForeignActionsAndSecretsDoNotLeak()
    {
        const string credentialSentinel = "credential-sentinel-action-approval";
        const string artifactTamperSentinel = "artifact-tamper-sentinel";
        var tenantAKey = OperatorKey + credentialSentinel;
        await using var database = await ActionApprovalDatabase.CreateAsync(postgres);
        var originA = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString, "tenant-a");
        var originB = await ActionApprovalTestSupport.SeedOriginAsync(database.ConnectionString, "tenant-b");
        using var repositories = ActionApprovalTestSupport.CreateServices(database.ConnectionString);
        var proposalRepository = repositories.GetRequiredService<IActionProposalRepository>();
        var actionA = (await proposalRepository.CreateAsync(
            ActionApprovalTestSupport.Proposal(originA, "api-tenant-a"),
            TestContext.Current.CancellationToken)).Action;
        var actionB = (await proposalRepository.CreateAsync(
            ActionApprovalTestSupport.Proposal(originB, "api-tenant-b"),
            TestContext.Current.CancellationToken)).Action;
        using var expiredRepositories = ActionApprovalTestSupport.CreateServices(
            database.ConnectionString,
            timeProvider: new ManualTimeProvider(DateTimeOffset.UtcNow.AddHours(-2)));
        var expiredAction = (await expiredRepositories.GetRequiredService<IActionProposalRepository>().CreateAsync(
            ActionApprovalTestSupport.Proposal(originA, "api-expired"),
            TestContext.Current.CancellationToken)).Action;
        await ActionApprovalTestSupport.ExecuteAsync(database.ConnectionString, """
            DROP TRIGGER trg_action_review_artifact_immutable ON incidentcompass.triage_artifacts;
            UPDATE incidentcompass.triage_artifacts
            SET redacted_payload = jsonb_build_object('tampered', @sentinel)
            WHERE id = @artifact_id;
            """, ("sentinel", artifactTamperSentinel), ("artifact_id", actionA.ProposalArtifactId));

        var capturedLogs = new List<string>();
        using var factory = CreateEnabledFactory(database.ConnectionString, capturedLogs, credentialSentinel);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var foreignGet = await SendWithKeyAsync(
            client, HttpMethod.Get, $"/api/v1/action-approvals/{actionB.Id}", tenantAKey);
        var foreignDecision = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionB.Id}/approve", tenantAKey,
            new
            {
                payloadSha256 = actionB.PayloadSha256,
                approvalSha256 = actionB.ApprovalSha256,
                tenantId = credentialSentinel,
                actor = credentialSentinel,
                adapterRoute = credentialSentinel
            });
        var ownGet = await SendWithKeyAsync(
            client, HttpMethod.Get, $"/api/v1/action-approvals/{actionA.Id}", tenantAKey);
        var ownJson = await ownGet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var ownList = await SendWithKeyAsync(
            client, HttpMethod.Get, "/api/v1/action-approvals", tenantAKey);
        var ownListJson = await ownList.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var hashMismatch = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionA.Id}/approve", tenantAKey,
            new { payloadSha256 = new string('c', 64), approvalSha256 = actionA.ApprovalSha256 });
        var malformed = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionA.Id}/approve", tenantAKey,
            new { payloadSha256 = "malformed", approvalSha256 = actionA.ApprovalSha256 });
        var expired = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{expiredAction.Id}/approve", tenantAKey,
            new
            {
                payloadSha256 = expiredAction.PayloadSha256,
                approvalSha256 = expiredAction.ApprovalSha256
            });
        var ownApproval = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionA.Id}/approve", tenantAKey,
            new { payloadSha256 = actionA.PayloadSha256, approvalSha256 = actionA.ApprovalSha256 });
        var repeatedApproval = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionA.Id}/approve", tenantAKey,
            new { payloadSha256 = actionA.PayloadSha256, approvalSha256 = actionA.ApprovalSha256 });
        var tenantBGet = await SendWithKeyAsync(
            client, HttpMethod.Get, $"/api/v1/action-approvals/{actionB.Id}", OtherOperatorKey);
        var tenantBReject = await SendWithKeyAsync(
            client, HttpMethod.Post, $"/api/v1/action-approvals/{actionB.Id}/reject", OtherOperatorKey,
            new
            {
                payloadSha256 = actionB.PayloadSha256,
                approvalSha256 = actionB.ApprovalSha256,
                reason = "Tenant B rejected this action."
            });

        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDecision.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        Assert.Contains(actionA.Id.ToString(), ownListJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(actionB.Id.ToString(), ownListJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict, hashMismatch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownApproval.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeatedApproval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tenantBGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tenantBReject.StatusCode);
        Assert.Contains("Investigate incident", ownJson, StringComparison.Ordinal);
        Assert.DoesNotContain(artifactTamperSentinel, ownJson, StringComparison.Ordinal);
        Assert.DoesNotContain(credentialSentinel, ownJson, StringComparison.Ordinal);
        Assert.DoesNotContain(credentialSentinel, string.Join('\n', capturedLogs), StringComparison.Ordinal);
        Assert.Equal("rejected", await ReadActionStateAsync(database.ConnectionString, actionB.Id));
        Assert.Equal("expired", await ReadActionStateAsync(database.ConnectionString, expiredAction.Id));
        Assert.Equal("key:operator-a", await ReadDecisionActorAsync(database.ConnectionString, actionA.Id));
        Assert.Equal("key:operator-b", await ReadDecisionActorAsync(database.ConnectionString, actionB.Id));
        Assert.Equal(0, await CountSentinelAsync(database.ConnectionString, credentialSentinel));
    }

    private static WebApplicationFactory<Program> CreateEnabledFactory(
        string connectionString,
        List<string> capturedLogs,
        string credentialSentinel) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new CapturingLoggerProvider(capturedLogs));
            });
            builder.UseExplicitMockProviders();
            builder.UseSetting("ConnectionStrings:ActionApprovalTests", connectionString);
            builder.UseSetting("IncidentCompass:Postgres:ConnectionStringName", "ActionApprovalTests");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:PermitLimit", "100");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:WindowSeconds", "300");
            SetCredential(builder, 0, "operator-a", "tenant-a", OperatorKey + credentialSentinel);
            SetCredential(builder, 1, "operator-b", "tenant-b", OtherOperatorKey);
            builder.ConfigureTestServices(static services => services.RemoveAll<IHostedService>());
        });

    private static void SetCredential(
        IWebHostBuilder builder,
        int index,
        string keyId,
        string tenantId,
        string key)
    {
        var prefix = $"IncidentCompass:ApiKeyAuth:Credentials:{index}";
        builder.UseSetting($"{prefix}:KeyId", keyId);
        builder.UseSetting($"{prefix}:TenantId", tenantId);
        builder.UseSetting($"{prefix}:Sha256Digest", Digest(key));
    }

    private static async Task<HttpResponseMessage> SendWithKeyAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        string key,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-IncidentCompass-Key", key);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadActionStateAsync(string connectionString, Guid actionId) =>
        Convert.ToString(await ActionApprovalTestSupport.ScalarAsync(
            connectionString,
            "SELECT state FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", actionId)))!;

    private static async Task<string> ReadDecisionActorAsync(string connectionString, Guid actionId) =>
        Convert.ToString(await ActionApprovalTestSupport.ScalarAsync(
            connectionString,
            "SELECT decision_actor FROM incidentcompass.action_approvals WHERE id = @id;",
            ("id", actionId)))!;

    private static Task<long> CountSentinelAsync(string connectionString, string sentinel) =>
        ActionApprovalTestSupport.CountAsync(connectionString, """
            SELECT
                (SELECT count(*) FROM incidentcompass.action_approvals
                 WHERE convert_from(canonical_payload, 'UTF8') LIKE '%' || @sentinel || '%'
                    OR review_summary LIKE '%' || @sentinel || '%'
                    OR logical_target_id LIKE '%' || @sentinel || '%')
              + (SELECT count(*) FROM incidentcompass.triage_ledger
                 WHERE coalesce(role, '') LIKE '%' || @sentinel || '%'
                    OR coalesce(rationale, '') LIKE '%' || @sentinel || '%'
                    OR coalesce(payload_ref, '') LIKE '%' || @sentinel || '%');
            """, ("sentinel", sentinel));

    private sealed class CountingReviewRepository : IActionApprovalReviewRepository
    {
        public int AccessCount { get; private set; }
        public string? LastTenantId { get; private set; }
        public ActionDecisionRequest? LastDecision { get; private set; }

        public Task<IReadOnlyList<ActionApprovalRecord>> ListAsync(
            ActionApprovalListFilter filter,
            string tenantId,
            CancellationToken cancellationToken)
        {
            AccessCount++;
            LastTenantId = tenantId;
            return Task.FromResult<IReadOnlyList<ActionApprovalRecord>>([]);
        }

        public Task<(ActionApprovalRecord Action, IReadOnlyList<ActionApprovalProvenance> Provenance)?> FindAsync(
            Guid actionId,
            string tenantId,
            CancellationToken cancellationToken)
        {
            AccessCount++;
            LastTenantId = tenantId;
            return Task.FromResult<(ActionApprovalRecord, IReadOnlyList<ActionApprovalProvenance>)?>(null);
        }

        public Task<ActionDecisionResult> DecideAsync(
            ActionDecisionRequest request,
            CancellationToken cancellationToken)
        {
            AccessCount++;
            LastTenantId = request.TenantId;
            LastDecision = request;
            return Task.FromResult(new ActionDecisionResult(ActionDecisionOutcome.NotFound, null, null));
        }
    }

    private sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add(formatter(state, exception));
                if (exception is not null)
                {
                    messages.Add(exception.ToString());
                }
            }
        }
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
}
