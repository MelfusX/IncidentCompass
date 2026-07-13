using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TriageReportReadEndpointTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task GetTriageReportById_CitedPriorReportShowsUntrustedMarker()
    {
        using var scope = await CreateScopeAsync();
        var serviceName = "prior-report-svc-" + Guid.NewGuid().ToString("N");
        var first = await PostIngestAsync(scope.Client, serviceName);
        await RunClaimedJobAsync(scope, first.JobId!.Value, "worker-prior-1");
        await ExecuteAsync(scope.ConnectionString, """
            UPDATE incidentcompass.faults
            SET created_at_utc = now() - interval '32 minutes',
                completed_at_utc = now() - interval '31 minutes'
            WHERE id = @fault_id;
            """, ("fault_id", first.FaultId));

        var recurrence = await PostIngestAsync(scope.Client, serviceName);
        await RunClaimedJobAsync(scope, recurrence.JobId!.Value, "worker-prior-2");

        var reportId = await ScalarAsync<Guid>(scope.ConnectionString, "SELECT id FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;", ("fault_id", recurrence.FaultId));
        var response = await scope.Client.GetAsync("/api/v1/triage-reports/" + reportId, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TriageReportDetailsDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        var priorEvidence = Assert.Single(body.Evidence, evidence => evidence.Kind == "PriorReport");
        Assert.Equal("PriorReport", priorEvidence.ArtifactKind);
        Assert.Equal("untrusted-prior-hypothesis", priorEvidence.ArtifactPayload.GetProperty("trust").GetString());
    }

    private async Task<TestScope> CreateScopeAsync()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(connectionString);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseExplicitMockProviders();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddScoped<IAiModelClient, PriorReportCitingModelClient>();
            });
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        return new TestScope(factory, client, connectionString);
    }

    private static async Task RunClaimedJobAsync(TestScope scope, Guid expectedJobId, string workerId)
    {
        await ExecuteAsync(scope.ConnectionString, """
            UPDATE incidentcompass.triage_jobs
            SET status = 'DeadLettered', locked_by = NULL, locked_until_utc = NULL
            WHERE id <> @job_id AND status = 'Pending';
            """, ("job_id", expectedJobId));

        using var serviceScope = scope.Factory.Services.CreateScope();
        var runner = serviceScope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
        var claimed = await runner.ClaimNextAsync(workerId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.NotNull(claimed);
        Assert.Equal(expectedJobId, claimed.Id);
        await runner.ProcessClaimedAsync(
            claimed,
            workerId,
            new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);
    }

    private static async Task<IngestSignalResponseDto> PostIngestAsync(HttpClient client, string serviceName)
    {
        var unique = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new TesterEnvelopeDto(
                "tester",
                serviceName,
                "prod",
                DateTimeOffset.UtcNow,
                new TesterAttributesDto("TimeoutException", "prior report timeout " + unique, "/prior")),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.NotNull(body.JobId);
        return body;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class PriorReportCitingModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            var prior = TryFindPromptArtifactId(request, "PriorReport");
            var referenceId = prior ?? TryFindPromptArtifactId(request, "TriggerSignal") ?? throw new InvalidOperationException("No citable prompt artifact was found.");
            return Task.FromResult(Response(request, [PublishCall(referenceId)]));
        }

        private static AiModelResponse Response(AiModelRequest request, IReadOnlyList<AiToolCall> toolCalls)
        {
            return new AiModelResponse("publish", request.Model, "prior-report-test", new AiModelUsage(10, 5, 15), request.CorrelationId, toolCalls);
        }

        private static AiToolCall PublishCall(string referenceId)
        {
            using var arguments = JsonDocument.Parse("{\"report_json\":{\"status\":\"Completed\",\"summary\":\"Prior report citation.\",\"classification\":\"SimpleKnownError\",\"confidence\":\"Medium\",\"documentationFit\":\"Missing\",\"evidence\":[{\"referenceId\":\"" + referenceId + "\"}],\"limitations\":[],\"recommendedNextAction\":\"Review cited prior context.\"}}");
            return new AiToolCall("publish-prior", "publish_report", "v1", arguments.RootElement.Clone());
        }

        private static string? TryFindPromptArtifactId(AiModelRequest request, string kind)
        {
            var prompt = request.Messages.First(static message => message.Role == AiMessageRole.User).Content;
            foreach (var line in prompt.Split('\n'))
            {
                if (!line.Contains("kind=" + kind, StringComparison.Ordinal))
                {
                    continue;
                }

                var marker = "artifact:";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                return line[(start + marker.Length)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }

            return null;
        }
    }

    private sealed record TestScope(WebApplicationFactory<Program> Factory, HttpClient Client, string ConnectionString) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed record TesterAttributesDto(string ErrorType, string ErrorMessage, string HttpRoute);

    private sealed record TesterEnvelopeDto(
        string SourceKind,
        string ServiceName,
        string Environment,
        DateTimeOffset ObservedAtUtc,
        TesterAttributesDto Attributes);

    private sealed record IngestSignalResponseDto(
        Guid SignalId,
        Guid FaultId,
        bool IsNewFault,
        bool IsNewJob,
        bool IsSuppressed,
        Guid? JobId,
        string? ConfigHash);

    private sealed record TriageReportDetailsDto(IReadOnlyList<TriageEvidenceDto> Evidence);

    private sealed record TriageEvidenceDto(
        string Kind,
        string ArtifactKind,
        JsonElement ArtifactPayload);
}

