using System.Net;
using System.Net.Http.Json;
using Google.Protobuf;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using static IncidentCompass.IntegrationTests.IncidentIngestionTestSupport;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class IncidentIngestionTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task IngestSignal_TesterEnvelope_CreatesSignalFaultAndPendingJobBoundToConfigHash()
    {
        using var scope = await CreateScopeAsync(postgres);

        var response = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            TesterEnvelope("payments-api", "prod", "TimeoutException", "Checkout timed out", "/checkout"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ingested = await response.Content.ReadFromJsonAsync<IngestionSignalResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);
        Assert.NotEqual(Guid.Empty, ingested.SignalId);
        Assert.NotEqual(Guid.Empty, ingested.FaultId);
        Assert.NotNull(ingested.JobId);
        Assert.NotEqual(Guid.Empty, ingested.JobId!.Value);
        Assert.False(string.IsNullOrWhiteSpace(ingested.ConfigHash));

        var faultResponse = await scope.Client.GetAsync($"/api/v1/faults/{ingested.FaultId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, faultResponse.StatusCode);
        var fault = await faultResponse.Content.ReadFromJsonAsync<IngestionFaultDetails>(TestContext.Current.CancellationToken);
        Assert.NotNull(fault);
        Assert.Equal("Queued", fault.Status);
        Assert.Equal("Strong", fault.FingerprintStrength);
        Assert.NotNull(fault.Job);
        Assert.Equal(ingested.ConfigHash, fault.Job!.ConfigHash);

        var snapshotExists = await ScalarAsync<bool>(
            scope.ConnectionString,
            """
            SELECT EXISTS (
                SELECT 1 FROM incidentcompass.triage_config_snapshots
                WHERE config_hash = @config_hash
                  AND serialized_config IS NOT NULL
                  AND instructions IS NOT NULL
                  AND serialized_config::text <> '{}'
                  AND instructions::text <> '{}'
            );
            """,
            ("config_hash", ingested.ConfigHash!));
        Assert.True(snapshotExists);
    }

    public async Task OtlpTraceExport_ErrorSpanFlowsThroughTheOtelNormalizer()
    {
        using var scope = await CreateScopeAsync(postgres);
        var export = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes =
                        {
                            Attribute("service.name", "otel-checkout"),
                            Attribute("deployment.environment.name", "staging")
                        }
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span
                                {
                                    Name = "POST /checkout",
                                    TraceId = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
                                    SpanId = ByteString.CopyFrom(Enumerable.Range(17, 8).Select(value => (byte)value).ToArray()),
                                    ParentSpanId = ByteString.CopyFrom(Enumerable.Range(25, 8).Select(value => (byte)value).ToArray()),
                                    StartTimeUnixNano = 1_700_000_000_000_000_000,
                                    EndTimeUnixNano = 1_700_000_000_250_000_000,
                                    Status = new Status { Code = Status.Types.StatusCode.Error, Message = "upstream timeout" },
                                    Attributes =
                                    {
                                        Attribute("exception.type", "TimeoutException"),
                                        Attribute("exception.message", "Checkout timed out"),
                                        Attribute("http.request.method", "POST"),
                                        Attribute("http.route", "/checkout"),
                                        Attribute("http.response.status_code", 504L)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        using var content = new ByteArrayContent(export.ToByteArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        var response = await scope.Client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1L, await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.signals WHERE service_name = 'otel-checkout';"));
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT trace_id FROM incidentcompass.signals WHERE service_name = 'otel-checkout';"));
        Assert.Equal("191a1b1c1d1e1f20", await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT parent_span_id FROM incidentcompass.signals WHERE service_name = 'otel-checkout';"));
        Assert.Equal("TimeoutException", await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT error_type FROM incidentcompass.signals WHERE service_name = 'otel-checkout';"));
        Assert.Equal(1L, await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_jobs AS job JOIN incidentcompass.faults AS fault ON fault.id = job.fault_id WHERE fault.service_name = 'otel-checkout';"));
    }

    [DockerAvailableFact]
    public async Task OtlpLogExport_DistinctUnidentifiedZeroTimestampRecordsAreNotDeduplicated()
    {
        using var scope = await CreateScopeAsync(postgres);
        var export = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = new Resource
                    {
                        Attributes =
                        {
                            Attribute("service.name", "otlp-log-delivery-key"),
                            Attribute("deployment.environment.name", "test")
                        }
                    },
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords =
                            {
                                ErrorLog("zero timestamp record"),
                                ErrorLog("zero timestamp record")
                            }
                        }
                    }
                }
            }
        };
        var payload = export.ToByteArray();

        using (var firstContent = new ByteArrayContent(payload))
        {
            firstContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
            var firstResponse = await scope.Client.PostAsync("/v1/logs", firstContent, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var secondContent = new ByteArrayContent(payload))
        {
            secondContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
            var secondResponse = await scope.Client.PostAsync("/v1/logs", secondContent, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        }

        Assert.Equal(2L, await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.signals WHERE service_name = @service_name;",
            ("service_name", "otlp-log-delivery-key")));
        Assert.Equal(2L, await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(DISTINCT delivery_key) FROM incidentcompass.signals WHERE service_name = @service_name;",
            ("service_name", "otlp-log-delivery-key")));
    }

    private static LogRecord ErrorLog(string body) => new()
    {
        SeverityText = "ERROR",
        Body = new AnyValue { StringValue = body },
        Attributes =
        {
            Attribute("exception.type", "TimeoutException"),
            Attribute("exception.message", body)
        }
    };
    private static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static KeyValue Attribute(string key, long value) =>
        new() { Key = key, Value = new AnyValue { IntValue = value } };

    [DockerAvailableFact]
    public async Task IngestSignal_TypedFieldsAreRedactedBeforePersistence()
    {
        using var scope = await CreateScopeAsync(postgres);
        const string rawSecret = "abcdef1234567890";
        var secretText = "Bearer " + rawSecret;
        var response = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                sourceKind = "tester",
                serviceName = "service " + secretText,
                environment = "environment " + secretText,
                severity = "severity " + secretText,
                observedAtUtc = DateTimeOffset.UtcNow,
                correlation = new
                {
                    traceId = "trace " + secretText,
                    spanId = "span " + secretText,
                    externalId = "external " + secretText
                },
                attributes = new
                {
                    errorType = "type " + secretText,
                    errorMessage = "message " + secretText,
                    operationName = "operation " + secretText,
                    httpMethod = "method " + secretText,
                    httpRoute = "route " + secretText
                }
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ingested = await response.Content.ReadFromJsonAsync<IngestionSignalResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);

        var persisted = await ScalarAsync<string>(
            scope.ConnectionString,
            """
            SELECT concat_ws('|', external_id, trace_id, span_id, service_name, environment,
                                    operation_name, severity, error_type, error_message,
                                    http_method, http_route)
            FROM incidentcompass.signals
            WHERE id = @signal_id;
            """,
            ("signal_id", ingested.SignalId));

        Assert.DoesNotContain(rawSecret, persisted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", persisted, StringComparison.Ordinal);
    }

    [DockerAvailableFact]
    public async Task TriageConfigurationRepository_GetByHashAsync_RehydratesPersistedSnapshot()
    {
        using var scope = await CreateScopeAsync(postgres);
        const string configHash = "snapshot-rehydrate-test-hash";
        await InsertSnapshotAsync(scope.ConnectionString, configHash);

        using var serviceScope = scope.Factory.Services.CreateScope();
        var repository = serviceScope.ServiceProvider.GetRequiredService<ITriageConfigurationRepository>();

        var configuration = await repository.GetByHashAsync(configHash, TestContext.Current.CancellationToken);

        Assert.Equal(configHash, configuration.ConfigHash);
        Assert.Equal("snapshot-analysis-model", configuration.Routes["analysis-chat"].Model);
        Assert.Equal("snapshot orchestrator instructions", configuration.Orchestrator.Instructions);
        Assert.Equal("snapshot analysis instructions", configuration.Roles["analysis"].Instructions);
        Assert.Equal("{ \"type\": \"object\", \"additionalProperties\": false }", configuration.Roles["analysis"].OutputSchema);
        Assert.Equal("attempt", Assert.Single(configuration.Rules).Scope);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_NonUtcObservedAt_NormalizesBeforePostgresInsert()
    {
        using var scope = await CreateScopeAsync(postgres);
        var observed = new DateTimeOffset(2026, 7, 1, 15, 0, 0, TimeSpan.FromHours(3));

        var ingested = await PostIngestAsync(
            scope.Client,
            TesterEnvelope("offset-observed-svc", "prod", "TimeoutException", "Offset probe timed out", "/offset-probe", observed));

        var storedObserved = await ScalarAsync<DateTime>(
            scope.ConnectionString,
            "SELECT observed_at_utc FROM incidentcompass.signals WHERE id = @signal_id;",
            ("signal_id", ingested.SignalId));

        Assert.Equal(DateTimeKind.Utc, storedObserved.Kind);
        Assert.Equal(observed.UtcDateTime, storedObserved);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_DisallowedSourceKind_ReturnsBadRequest()
    {
        using var scope = await CreateScopeAsync(postgres);

        var webhookResponse = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            TesterEnvelope("some-service", "prod", "Error", "message", "/route") with { SourceKind = "webhook" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, webhookResponse.StatusCode);
        using (var problem = await ReadJsonAsync(webhookResponse))
        {
            Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("sourceKind", out var sourceErrors));
            Assert.Contains(
                sourceErrors.EnumerateArray(),
                error => error.GetString()!.Contains("no registered normalizer", StringComparison.Ordinal));
        }

        var bogusResponse = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            TesterEnvelope("some-service", "prod", "Error", "message", "/route") with { SourceKind = "bogus" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bogusResponse.StatusCode);
    }


    [DockerAvailableFact]
    public async Task IngestSignal_NewFault_MaterializesTriggerSignalAndNeighborSetJobArtifacts()
    {
        using var scope = await CreateScopeAsync(postgres);
        var envelope = TesterEnvelope("artifact-check-svc", "prod", "TimeoutException", "Artifact check timed out", "/artifact-check");

        var ingested = await PostIngestAsync(scope.Client, envelope);

        var artifacts = await QueryArtifactKindsAsync(scope.ConnectionString, ingested.JobId!.Value);

        Assert.Contains(artifacts, artifact => artifact.Kind == "TriggerSignal" && artifact.Attempt is null);
        var neighborSet = Assert.Single(artifacts, artifact => artifact.Kind == "NeighborSet");
        Assert.Null(neighborSet.Attempt);

        var neighborCount = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'neighborCount' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", ingested.JobId.Value));
        Assert.Equal("1", neighborCount);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_NewFaultFailure_RollsBackSignalFaultJobAndArtifacts()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        const string serviceName = "rollback-probe-svc";

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseExplicitMockProviders();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITriageArtifactRepository>();
                services.AddScoped<ITriageArtifactRepository, ThrowingTriageArtifactRepository>();
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            TesterEnvelope(serviceName, "prod", "TimeoutException", "Rollback probe timed out", "/rollback-probe"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var signalCount = await ScalarAsync<long>(
            connectionString,
            "SELECT COUNT(*) FROM incidentcompass.signals WHERE service_name = @service_name;",
            ("service_name", serviceName));
        var faultCount = await ScalarAsync<long>(
            connectionString,
            "SELECT COUNT(*) FROM incidentcompass.faults WHERE service_name = @service_name;",
            ("service_name", serviceName));
        var jobCount = await ScalarAsync<long>(
            connectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_jobs j JOIN incidentcompass.faults f ON f.id = j.fault_id WHERE f.service_name = @service_name;",
            ("service_name", serviceName));
        var artifactCount = await ScalarAsync<long>(
            connectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_artifacts a JOIN incidentcompass.triage_jobs j ON j.id = a.job_id JOIN incidentcompass.faults f ON f.id = j.fault_id WHERE f.service_name = @service_name;",
            ("service_name", serviceName));

        Assert.Equal(0, signalCount);
        Assert.Equal(0, faultCount);
        Assert.Equal(0, jobCount);
        Assert.Equal(0, artifactCount);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_TenantIdInRequestBody_IsIgnoredAndDefaultTenantIsStamped()
    {
        using var scope = await CreateScopeAsync(postgres);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incidents")
        {
            Content = JsonContent.Create(new
            {
                sourceKind = "tester",
                serviceName = "tenant-spoof-svc",
                environment = "prod",
                observedAtUtc = DateTimeOffset.UtcNow,
                tenantId = "attacker-tenant",
                attributes = new
                {
                    errorType = "TimeoutException",
                    errorMessage = "Tenant spoof probe timed out",
                    httpRoute = "/tenant-spoof-probe",
                },
            }),
        };

        var response = await scope.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ingested = await response.Content.ReadFromJsonAsync<IngestionSignalResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);

        var tenantId = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT tenant_id FROM incidentcompass.signals WHERE id = @signal_id;",
            ("signal_id", ingested.SignalId));
        Assert.Equal("local", tenantId);
    }

    private sealed class ThrowingTriageArtifactRepository : ITriageArtifactRepository
    {
        public Task InsertAsync(TriageArtifact artifact, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Injected artifact persistence failure.");
        }

        public Task ReplaceJobLevelAsync(TriageArtifact artifact, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Injected artifact persistence failure.");
        }
    }

}
