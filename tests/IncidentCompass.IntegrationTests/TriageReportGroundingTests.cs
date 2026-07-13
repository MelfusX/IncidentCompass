using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Redaction;
using IncidentCompass.Infrastructure.ModelGateway.Mock;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TriageReportGroundingTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task ProcessClaimedAsync_WorkerOutputEvidenceIsRejectedThenReprompted()
    {
        using var scope = await CreateScopeAsync(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped<IAiModelClient, WorkerOutputThenTriggerEvidenceModelClient>();
        });
        var ingested = await PostIngestAsync(scope.Client, "worker-output-grounding");

        await RunClaimedJobAsync(scope, ingested.JobId!.Value, "worker-grounding", maxAttempts: 1);

        var jobStatus = await ScalarAsync<string>(scope.ConnectionString, "SELECT status FROM incidentcompass.triage_jobs WHERE id = @job_id;", ("job_id", ingested.JobId.Value));
        var evidenceKinds = await ReadEvidenceKindsAsync(scope.ConnectionString, ingested.FaultId);
        var workerOutputEvidence = await ScalarAsync<long>(scope.ConnectionString, """
            SELECT COUNT(*)
            FROM incidentcompass.triage_evidence e
            JOIN incidentcompass.triage_artifacts a ON a.id = e.artifact_id
            JOIN incidentcompass.triage_reports r ON r.id = e.report_id
            WHERE r.fault_id = @fault_id AND a.kind = 'WorkerOutput';
            """, ("fault_id", ingested.FaultId));

        Assert.Equal("Succeeded", jobStatus);
        Assert.Contains("TriggerSignal", evidenceKinds);
        Assert.Equal(0, workerOutputEvidence);
    }

    [DockerAvailableFact]
    public async Task ProcessClaimedAsync_RedactsCraftedMarkersBeforePersistenceArtifactsAndModelRequests()
    {
        const string rawIdentifier = "raw-user-redaction-e2e@example.test";
        const string configuredAttributeSecret = "configured-customer-account-redaction-e2e";
        var crafted = "[PSEUDONYM:v1:" + new string('a', 64) + ":" + new string('b', 64) + "]";
        var requests = new ConcurrentQueue<AiModelRequest>();
        using var scope = await CreateScopeAsync(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped<IAiModelClient>(_ => new CapturingMockAiModelClient(requests));
        });
        var response = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                sourceKind = "tester",
                serviceName = "redaction-e2e-" + Guid.NewGuid().ToString("N"),
                environment = "prod",
                observedAtUtc = DateTimeOffset.UtcNow,
                attributes = new
                {
                    errorType = "TimeoutException",
                    errorMessage = "request by " + rawIdentifier,
                    httpRoute = "/redaction-e2e",
                    user = new { id = rawIdentifier },
                    password = crafted,
                    customer = new
                    {
                        account = new { id = configuredAttributeSecret }
                    }
                },
                payload = new
                {
                    user = new { email = rawIdentifier },
                    password = crafted
                }
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ingested = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);
        Assert.NotNull(ingested.JobId);
        await RunClaimedJobAsync(scope, ingested.JobId.Value, "worker-redaction-e2e", 1);

        var signalText = await ScalarAsync<string>(scope.ConnectionString,
            "SELECT concat_ws('|', error_message, summary, attributes::text, body::text) FROM incidentcompass.signals WHERE id = @signal_id;", ("signal_id", ingested.SignalId));
        var triggerPayload = await ScalarAsync<string>(scope.ConnectionString,
            "SELECT redacted_payload::text FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'TriggerSignal';", ("job_id", ingested.JobId.Value));
        var canonicalUserId = await ScalarAsync<string>(scope.ConnectionString,
            "SELECT attributes #>> '{user,id}' FROM incidentcompass.signals WHERE id = @signal_id;", ("signal_id", ingested.SignalId));
        var password = await ScalarAsync<string>(scope.ConnectionString,
            "SELECT attributes->>'password' FROM incidentcompass.signals WHERE id = @signal_id;", ("signal_id", ingested.SignalId));
        var authorization = await ScalarAsync<string>(scope.ConnectionString,
            "SELECT attributes #>> '{customer,account,id}' FROM incidentcompass.signals WHERE id = @signal_id;", ("signal_id", ingested.SignalId));

        Assert.Equal("[REDACTED]", password);
        Assert.Equal("[REDACTED]", authorization);
        Assert.DoesNotContain(rawIdentifier, signalText, StringComparison.Ordinal);
        Assert.DoesNotContain(crafted, signalText, StringComparison.Ordinal);
        Assert.DoesNotContain(configuredAttributeSecret, signalText, StringComparison.Ordinal);
        Assert.DoesNotContain(rawIdentifier, triggerPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(crafted, triggerPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(configuredAttributeSecret, triggerPayload, StringComparison.Ordinal);
        var modelText = string.Join("\n", requests.SelectMany(request => request.Messages).Select(message => message.Content));
        Assert.NotEmpty(requests);
        Assert.DoesNotContain(rawIdentifier, modelText, StringComparison.Ordinal);
        Assert.DoesNotContain(crafted, modelText, StringComparison.Ordinal);
        Assert.DoesNotContain(configuredAttributeSecret, modelText, StringComparison.Ordinal);
        var pseudonymizer = scope.Factory.Services.GetRequiredService<UserIdentifierPseudonymizer>();
        Assert.True(pseudonymizer.IsCanonicalPseudonym(JsonValue.Create(canonicalUserId), "user.id"));
    }
    [DockerAvailableFact]
    public async Task ProcessClaimedAsync_UnverifiableQuoteIsDroppedButCitationPersists()
    {
        using var scope = await CreateScopeAsync(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped<IAiModelClient, InvalidQuoteModelClient>();
        });
        var ingested = await PostIngestAsync(scope.Client, "quote-drop");

        await RunClaimedJobAsync(scope, ingested.JobId!.Value, "worker-quote", maxAttempts: 1);

        var row = await ReadSingleEvidenceAsync(scope.ConnectionString, ingested.FaultId);
        Assert.Equal("TriggerSignal", row.Kind);
        Assert.Null(row.Quote);
    }

    [DockerAvailableFact]
    public async Task ProcessClaimedAsync_VerifiableQuoteIsKept()
    {
        const string quote = "KEEP quote from trigger payload";
        using var scope = await CreateScopeAsync(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped<IAiModelClient>(_ => new ValidQuoteModelClient(quote));
        });
        var unique = Guid.NewGuid().ToString("N");
        var ingested = await PostIngestAsync(scope.Client, new TesterEnvelopeDto(
            "tester",
            "quote-keep-svc-" + unique,
            "prod",
            DateTimeOffset.UtcNow,
            new TesterAttributesDto("TimeoutException", quote, "/phase5")));
        Assert.NotNull(ingested.JobId);

        await RunClaimedJobAsync(scope, ingested.JobId.Value, "worker-quote-keep", maxAttempts: 1);

        var row = await ReadSingleEvidenceAsync(scope.ConnectionString, ingested.FaultId);
        Assert.Equal("TriggerSignal", row.Kind);
        Assert.Equal(quote, row.Quote);
    }

    [DockerAvailableFact]
    public async Task ProcessClaimedAsync_FinalCommitFailureLeavesNoReportOrPublishedEvent()
    {
        using var scope = await CreateScopeAsync(services =>
        {
            services.RemoveAll<ITriageReportFinalCommitFaultInjector>();
            services.AddScoped<ITriageReportFinalCommitFaultInjector, ThrowBeforeReportPublished>();
        });
        var ingested = await PostIngestAsync(scope.Client, "final-rollback");

        await RunClaimedJobAsync(scope, ingested.JobId!.Value, "worker-final-failure", maxAttempts: 3);

        var jobStatus = await ScalarAsync<string>(scope.ConnectionString, "SELECT status FROM incidentcompass.triage_jobs WHERE id = @job_id;", ("job_id", ingested.JobId.Value));
        var reports = await ScalarAsync<long>(scope.ConnectionString, "SELECT COUNT(*) FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;", ("fault_id", ingested.FaultId));
        var published = await ScalarAsync<long>(scope.ConnectionString, "SELECT COUNT(*) FROM incidentcompass.triage_ledger WHERE job_id = @job_id AND event_type = 'ReportPublished';", ("job_id", ingested.JobId.Value));
        var delegated = await ScalarAsync<long>(scope.ConnectionString, "SELECT COUNT(*) FROM incidentcompass.triage_ledger WHERE job_id = @job_id AND event_type = 'Delegated';", ("job_id", ingested.JobId.Value));

        Assert.Equal("RetryPending", jobStatus);
        Assert.True(delegated > 0);
        Assert.Equal(0, reports);
        Assert.Equal(0, published);
    }

    [DockerAvailableFact]
    public async Task PublishAsync_StaleAttemptIsRejectedByFence()
    {
        using var scope = await CreateScopeAsync();
        var ingested = await PostIngestAsync(scope.Client, "stale-fence");
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-stale-1");
        var triggerArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, claimed.Id, "TriggerSignal");
        await ExecuteAsync(scope.ConnectionString, """
            UPDATE incidentcompass.triage_jobs
            SET attempt = 2, locked_by = 'worker-stale-2', locked_until_utc = now() + interval '5 minutes'
            WHERE id = @job_id;
            """, ("job_id", claimed.Id));

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PublishAsync(
            claimed,
            "worker-stale-1",
            CreateReport(triggerArtifactId),
            TestContext.Current.CancellationToken));

        var reports = await ScalarAsync<long>(scope.ConnectionString, "SELECT COUNT(*) FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;", ("fault_id", ingested.FaultId));
        Assert.Equal(0, reports);
    }


    [DockerAvailableFact]
    public async Task PublishAsync_DerivesAndPersistsDocumentationFitFromCitedMemoryItems()
    {
        var cases = new (string[] DocumentationStatuses, DocumentationFitStatus ExpectedFit, string? ExpectedLimitation)[]
        {
            (["Current"], DocumentationFitStatus.Current, null),
            (["Current", "Stale"], DocumentationFitStatus.CurrentWithHistorical, null),
            (["Stale"], DocumentationFitStatus.StaleOnly, null),
            ([], DocumentationFitStatus.Missing, null),
            (["Unversioned"], DocumentationFitStatus.Missing, "Cited documentation is unversioned or service-mismatched, so its currentness cannot be assessed."),
            (["Current", "Current"], DocumentationFitStatus.MultipleCurrentDocuments, "Multiple current documents were cited; their compatibility requires operator review.")
        };
        foreach (var testCase in cases)
        {
            using var scope = await CreateScopeAsync();
            var ingested = await PostIngestAsync(scope.Client, "documentation-fit");
            var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-documentation-fit");
            var artifactIds = new List<Guid>();
            foreach (var documentationStatus in testCase.DocumentationStatuses)
            {
                artifactIds.Add(await InsertRetrievedMemoryArtifactAsync(
                    scope.ConnectionString,
                    claimed.Id,
                    claimed.Attempt,
                    documentationStatus));
            }

            if (artifactIds.Count == 0)
            {
                artifactIds.Add(await ReadArtifactIdAsync(scope.ConnectionString, claimed.Id, "TriggerSignal"));
            }

            using var serviceScope = scope.Factory.Services.CreateScope();
            var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
            var reportId = await repository.PublishAsync(
                claimed,
                "worker-documentation-fit",
                CreateReport(artifactIds[0]) with
                {
                    DocumentationFit = testCase.ExpectedFit,
                    Evidence = artifactIds.Select(static id => new TriageReportEvidenceReference(id.ToString(), null)).ToArray()
                },
                TestContext.Current.CancellationToken);

            var persistedFit = await ScalarAsync<string>(
                scope.ConnectionString,
                "SELECT documentation_fit FROM incidentcompass.triage_reports WHERE id = @report_id;",
                ("report_id", reportId));
            Assert.Equal(testCase.ExpectedFit.ToString(), persistedFit);

            var response = await scope.Client.GetAsync(
                "/api/v1/triage-reports/" + reportId,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var details = await response.Content.ReadFromJsonAsync<DocumentationFitDetailsDto>(TestContext.Current.CancellationToken);
            Assert.NotNull(details);
            Assert.Equal(testCase.ExpectedFit.ToString(), details.DocumentationFit);
            if (testCase.ExpectedLimitation is null)
            {
                Assert.Empty(details.Limitations);
            }
            else
            {
                Assert.Contains(testCase.ExpectedLimitation, details.Limitations);
            }
        }
    }

    [DockerAvailableFact]
    public async Task PublishAsync_RejectsModelDocumentationFitThatDisagreesWithCitedMemoryItems()
    {
        using var scope = await CreateScopeAsync();
        var ingested = await PostIngestAsync(scope.Client, "documentation-fit-rejection");
        var claimed = await ClaimAsync(scope, ingested.JobId!.Value, "worker-documentation-fit-rejection");
        var artifactId = await InsertRetrievedMemoryArtifactAsync(
            scope.ConnectionString,
            claimed.Id,
            claimed.Attempt,
            "Current");

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await Assert.ThrowsAsync<TriageReportValidationException>(() => repository.PublishAsync(
            claimed,
            "worker-documentation-fit-rejection",
            CreateReport(artifactId),
            TestContext.Current.CancellationToken));

        var reports = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;",
            ("fault_id", ingested.FaultId));
        Assert.Equal(0, reports);
    }
    [DockerAvailableFact]
    public async Task PublishAsync_RefreshedNeighborSetKeepsStableEvidenceReference()
    {
        using var scope = await CreateScopeAsync();
        var serviceName = "neighbor-refresh-svc-" + Guid.NewGuid().ToString("N");
        var envelope = new TesterEnvelopeDto(
            "tester",
            serviceName,
            "prod",
            DateTimeOffset.UtcNow,
            new TesterAttributesDto("TimeoutException", "Neighbor refresh timeout", "/neighbor-refresh"));
        var ingested = await PostIngestAsync(scope.Client, envelope);
        Assert.NotNull(ingested.JobId);
        var neighborArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, ingested.JobId!.Value, "NeighborSet");

        var attached = await PostIngestAsync(scope.Client, envelope with { ObservedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) });
        Assert.Equal(ingested.FaultId, attached.FaultId);
        Assert.False(attached.IsNewJob);

        var refreshedNeighborArtifactId = await ReadArtifactIdAsync(scope.ConnectionString, ingested.JobId.Value, "NeighborSet");
        Assert.Equal(neighborArtifactId, refreshedNeighborArtifactId);

        var claimed = await ClaimAsync(scope, ingested.JobId.Value, "worker-neighbor-refresh");
        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageReportRepository>();
        await repository.PublishAsync(
            claimed,
            "worker-neighbor-refresh",
            CreateReport(neighborArtifactId),
            TestContext.Current.CancellationToken);

        var evidence = await ReadSingleEvidenceAsync(scope.ConnectionString, ingested.FaultId);
        Assert.Equal("NeighborSet", evidence.Kind);
    }

    private async Task<TestScope> CreateScopeAsync(Action<IServiceCollection>? configureServices = null)
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(connectionString);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseExplicitMockProviders();
            builder.UseSetting("IncidentCompass:Pseudonymization:Salt", "redaction-e2e-salt");
            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        return new TestScope(factory, client, connectionString);
    }

    private static async Task RunClaimedJobAsync(TestScope scope, Guid expectedJobId, string workerId, int maxAttempts)
    {
        var claimed = await ClaimAsync(scope, expectedJobId, workerId);
        using var serviceScope = scope.Factory.Services.CreateScope();
        var runner = serviceScope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
        await runner.ProcessClaimedAsync(
            claimed,
            workerId,
            new TriageJobProcessingSettings(maxAttempts, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);
    }

    private static async Task<TriageJob> ClaimAsync(TestScope scope, Guid expectedJobId, string workerId)
    {
        using var serviceScope = scope.Factory.Services.CreateScope();
        var runner = serviceScope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
        var claimed = await runner.ClaimNextAsync(workerId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.NotNull(claimed);
        Assert.Equal(expectedJobId, claimed.Id);
        return claimed;
    }

    private static TriageReport CreateReport(Guid referenceId)
    {
        return new TriageReport(
            TriageReportStatus.Completed,
            "Direct stale attempt report.",
            "SimpleKnownError",
            "Medium",
            [new TriageReportEvidenceReference(referenceId.ToString(), null)],
            [],
            "Review the trigger signal.");
    }


    private static async Task<IngestSignalResponseDto> PostIngestAsync(HttpClient client, TesterEnvelopeDto envelope)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            envelope,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }

    private static async Task<IngestSignalResponseDto> PostIngestAsync(HttpClient client, string prefix)
    {
        var unique = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new TesterEnvelopeDto(
                "tester",
                prefix + "-svc-" + unique,
                "prod",
                DateTimeOffset.UtcNow,
                new TesterAttributesDto("TimeoutException", prefix + " timeout " + unique, "/phase5")),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.NotNull(body.JobId);
        return body;
    }

    private static async Task<IReadOnlyList<string>> ReadEvidenceKindsAsync(string connectionString, Guid faultId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT e.kind
            FROM incidentcompass.triage_evidence e
            JOIN incidentcompass.triage_reports r ON r.id = e.report_id
            WHERE r.fault_id = @fault_id
            ORDER BY e.created_at_utc, e.id;
            """, connection);
        command.Parameters.AddWithValue("fault_id", faultId);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<EvidenceRow> ReadSingleEvidenceAsync(string connectionString, Guid faultId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT e.kind, e.quote
            FROM incidentcompass.triage_evidence e
            JOIN incidentcompass.triage_reports r ON r.id = e.report_id
            WHERE r.fault_id = @fault_id;
            """, connection);
        command.Parameters.AddWithValue("fault_id", faultId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new EvidenceRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<Guid> ReadArtifactIdAsync(string connectionString, Guid jobId, string kind)
    {
        return await ScalarAsync<Guid>(connectionString, "SELECT id FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = @kind ORDER BY created_at_utc, id LIMIT 1;", ("job_id", jobId), ("kind", kind));
    }

    private static async Task<Guid> InsertRetrievedMemoryArtifactAsync(
        string connectionString,
        Guid jobId,
        int attempt,
        string documentationStatus)
    {
        var memoryItemId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        await ExecuteAsync(connectionString, """
            INSERT INTO incidentcompass.memory_items (
                id, tenant_id, kind, source, title, content, content_hash, version, tags, created_at_utc)
            VALUES (
                @id, 'demo', 'runbook', @source, 'Documentation fit test', @content, @content_hash, 1,
                ARRAY['documentation'], now());
            """,
            ("id", memoryItemId),
            ("source", "test://documentation-fit/" + unique),
            ("content", "Documentation status " + documentationStatus),
            ("content_hash", unique));
        await ExecuteAsync(connectionString, """
            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, domain_ref, redacted_payload, content_hash, created_at_utc)
            VALUES (
                @id, @job_id, @attempt, 'RetrievedItem', @domain_ref, @payload::jsonb, @content_hash, now());
            """,
            ("id", artifactId),
            ("job_id", jobId),
            ("attempt", attempt),
            ("domain_ref", "memory_item:" + memoryItemId),
            ("payload", JsonSerializer.Serialize(new { documentationStatus, score = 0.9 })),
            ("content_hash", unique));
        return artifactId;
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

    private sealed class CapturingMockAiModelClient(ConcurrentQueue<AiModelRequest> requests) : IAiModelClient
    {
        private readonly MockAiModelClient inner = new();

        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request);
            return inner.CompleteAsync(request, cancellationToken);
        }
    }
    private sealed class WorkerOutputThenTriggerEvidenceModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            if (!IsOrchestrator(request))
            {
                return Task.FromResult(Response(request, WorkerJson(), []));
            }

            if (!request.Messages.Any(static message => message.Role == AiMessageRole.Tool))
            {
                return Task.FromResult(Response(request, "delegate", [ToolCall("delegate-analysis", "delegate", "{\"role\":\"analysis\",\"task\":\"Analyze.\"}")]));
            }

            if (request.Messages.Any(static message => message.Content.Contains("publish_report_validation_failed", StringComparison.Ordinal)))
            {
                return Task.FromResult(Response(request, "publish valid", [PublishCall(request, FindPromptArtifactId(request, "TriggerSignal"), null)]));
            }

            var workerOutputId = FindToolResultArtifactId(request);
            return Task.FromResult(Response(request, "publish invalid", [PublishCall(request, workerOutputId, null)]));
        }
    }

    private sealed class ValidQuoteModelClient(string quote) : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            var referenceId = FindPromptArtifactId(request, "TriggerSignal");
            return Task.FromResult(Response(request, "publish", [PublishCall(request, referenceId, quote)]));
        }
    }

    private sealed class InvalidQuoteModelClient : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            var referenceId = FindPromptArtifactId(request, "TriggerSignal");
            return Task.FromResult(Response(request, "publish", [PublishCall(request, referenceId, "fabricated quote not present in artifact")]));
        }
    }

    private sealed class ThrowBeforeReportPublished : ITriageReportFinalCommitFaultInjector
    {
        public Task BeforeReportPublishedLedgerEventAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Injected final transaction failure before ReportPublished.");
        }
    }

    private static AiToolCall PublishCall(AiModelRequest request, string referenceId, string? quote)
    {
        var quoteJson = quote is null ? string.Empty : ",\"quote\":\"" + quote + "\"";
        return ToolCall("publish-" + Guid.NewGuid().ToString("N"), "publish_report", "{\"report_json\":{\"status\":\"Completed\",\"summary\":\"Grounded report.\",\"classification\":\"SimpleKnownError\",\"confidence\":\"Medium\",\"documentationFit\":\"Missing\",\"evidence\":[{\"referenceId\":\"" + referenceId + "\"" + quoteJson + "}],\"limitations\":[],\"recommendedNextAction\":\"Review the evidence.\"}}");
    }

    private static bool IsOrchestrator(AiModelRequest request)
    {
        var toolNames = request.Tools?.Select(static tool => tool.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        return toolNames.SetEquals(["delegate", "publish_report"]);
    }

    private static string WorkerJson()
    {
        return JsonSerializer.Serialize(new
        {
            keyFacts = new[] { "Analysis output is not citable evidence." },
            candidateClassification = "SimpleKnownError",
            needsDeeperContext = false,
            rationale = "Analysis completed."
        });
    }

    private static string FindPromptArtifactId(AiModelRequest request, string kind)
    {
        var prompt = request.Messages.First(static message => message.Role == AiMessageRole.User).Content;
        foreach (var line in prompt.Split('\n'))
        {
            if (line.Contains("kind=" + kind, StringComparison.Ordinal))
            {
                var marker = "artifact:";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                return line[(start + marker.Length)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }
        }

        throw new InvalidOperationException("Prompt did not contain artifact kind " + kind + ".");
    }

    private static string FindToolResultArtifactId(AiModelRequest request)
    {
        var toolResult = request.Messages.Last(static message => message.Role == AiMessageRole.Tool).Content;
        using var document = JsonDocument.Parse(toolResult);
        return document.RootElement.GetProperty("artifactId").GetString()!;
    }

    private static AiModelResponse Response(AiModelRequest request, string content, IReadOnlyList<AiToolCall> toolCalls)
    {
        return new AiModelResponse(content, request.Model, "report-grounding-test", new AiModelUsage(10, 5, 15), request.CorrelationId, toolCalls);
    }

    private static AiToolCall ToolCall(string id, string name, string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        return new AiToolCall(id, name, "v1", arguments.RootElement.Clone());
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

    private sealed record DocumentationFitDetailsDto(string DocumentationFit, IReadOnlyList<string> Limitations);

    private sealed record EvidenceRow(string Kind, string? Quote);
}
