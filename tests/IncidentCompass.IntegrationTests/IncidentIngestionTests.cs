using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Protobuf;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class IncidentIngestionTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task IngestSignal_TesterEnvelope_CreatesSignalFaultAndPendingJobBoundToConfigHash()
    {
        using var scope = await CreateScopeAsync();

        var response = await scope.Client.PostAsJsonAsync(
            "/api/v1/incidents",
            TesterEnvelope("payments-api", "prod", "TimeoutException", "Checkout timed out", "/checkout"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ingested = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);
        Assert.NotEqual(Guid.Empty, ingested.SignalId);
        Assert.NotEqual(Guid.Empty, ingested.FaultId);
        Assert.NotNull(ingested.JobId);
        Assert.NotEqual(Guid.Empty, ingested.JobId!.Value);
        Assert.False(string.IsNullOrWhiteSpace(ingested.ConfigHash));

        var faultResponse = await scope.Client.GetAsync($"/api/v1/faults/{ingested.FaultId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, faultResponse.StatusCode);
        var fault = await faultResponse.Content.ReadFromJsonAsync<FaultDetailsResponseDto>(TestContext.Current.CancellationToken);
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

    [DockerAvailableFact]
    public async Task IngestSignal_ServiceScopedFingerprintRulePersistsAndSeparatesGenerations()
    {
        using var scope = await CreateScopeAsync(useSmallSilenceWindowConfig: true);
        var selectedFirst = await PostIngestAsync(
            scope.Client,
            TesterEnvelope("rule-checkout", "prod", "TimeoutException", "node-a timed out", "/checkout"));
        var selectedSecond = await PostIngestAsync(
            scope.Client,
            TesterEnvelope("rule-checkout", "prod", "TimeoutException", "node-b timed out", "/checkout"));
        var defaultFirst = await PostIngestAsync(
            scope.Client,
            TesterEnvelope("default-checkout", "prod", "TimeoutException", "node-a timed out", "/checkout"));
        var defaultSecond = await PostIngestAsync(
            scope.Client,
            TesterEnvelope("default-checkout", "prod", "TimeoutException", "node-b timed out", "/checkout"));

        Assert.Equal(selectedFirst.FaultId, selectedSecond.FaultId);
        Assert.NotEqual(defaultFirst.FaultId, defaultSecond.FaultId);
        Assert.Equal("route-only-checkout", await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT grouping_rule_id FROM incidentcompass.signals WHERE id = @signal_id;",
            ("signal_id", selectedFirst.SignalId)));
        Assert.Equal(2, await ScalarAsync<int>(
            scope.ConnectionString,
            "SELECT grouping_rule_version FROM incidentcompass.faults WHERE id = @fault_id;",
            ("fault_id", selectedFirst.FaultId)));
        Assert.Equal("route-only-checkout", await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'groupingRuleId' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", selectedFirst.JobId!.Value)));
    }
    public async Task OtlpTraceExport_ErrorSpanFlowsThroughTheOtelNormalizer()
    {
        using var scope = await CreateScopeAsync();
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
        using var scope = await CreateScopeAsync();
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
        using var scope = await CreateScopeAsync();
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
        var ingested = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(
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
        using var scope = await CreateScopeAsync();
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
    public async Task IngestSignal_DuplicateFingerprint_AttachesToSameFaultWithoutNewJob()
    {
        using var scope = await CreateScopeAsync();
        var envelope = TesterEnvelope("checkout-api", "prod", "NullReferenceException", "Object reference not set", "/api/checkout/confirm");

        var first = await PostIngestAsync(scope.Client, envelope);
        var second = await PostIngestAsync(scope.Client, envelope);

        Assert.Equal(first.FaultId, second.FaultId);
        Assert.True(first.IsNewJob);
        Assert.False(second.IsNewJob);

        var jobCount = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_jobs WHERE fault_id = @fault_id;",
            ("fault_id", first.FaultId));
        Assert.Equal(1, jobCount);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_SilenceWindow_SuppressesThenReopensAsRecurrenceAfterWindowElapses()
    {
        using var scope = await CreateScopeAsync(useSmallSilenceWindowConfig: true);
        var envelope = TesterEnvelope("silence-window-svc", "prod", "TimeoutException", "Silence window probe timed out", "/silence-window-probe");

        var first = await PostIngestAsync(scope.Client, envelope);
        Assert.True(first.IsNewFault);

        await ExecuteAsync(
            scope.ConnectionString,
            "UPDATE incidentcompass.faults SET status = 'Completed', completed_at_utc = now() WHERE id = @id;",
            ("id", first.FaultId));

        var second = await PostIngestAsync(scope.Client, envelope);
        Assert.Equal(first.FaultId, second.FaultId);
        Assert.True(second.IsSuppressed);
        Assert.False(second.IsNewJob);

        // completed_at_utc must stay >= created_at_utc (faults_check), so back-date both together
        // far enough that completed_at_utc still clears the 1-minute silence window relative to
        // "now" when the third POST arrives.
        await ExecuteAsync(
            scope.ConnectionString,
            "UPDATE incidentcompass.faults SET created_at_utc = now() - interval '20 minutes', completed_at_utc = now() - interval '10 minutes' WHERE id = @id;",
            ("id", first.FaultId));

        var third = await PostIngestAsync(scope.Client, envelope);
        Assert.NotEqual(first.FaultId, third.FaultId);
        Assert.True(third.IsNewJob);

        var thirdFaultResponse = await scope.Client.GetAsync($"/api/v1/faults/{third.FaultId}", TestContext.Current.CancellationToken);
        var thirdFault = await thirdFaultResponse.Content.ReadFromJsonAsync<FaultDetailsResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(thirdFault);
        Assert.Equal(first.FaultId, thirdFault.RecurrenceOf);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_NonUtcObservedAt_NormalizesBeforePostgresInsert()
    {
        using var scope = await CreateScopeAsync();
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
    public async Task IngestSignal_NeighborCount_DeduplicatesRepeatedExternalId()
    {
        using var scope = await CreateScopeAsync();
        var envelope = TesterEnvelope("sql-dedupe-neighbor-svc", "prod", "TimeoutException", "SQL dedupe probe timed out", "/sql-dedupe-probe");

        var first = await PostIngestAsync(scope.Client, envelope with { Correlation = ExternalId("duplicate-external-id") });
        var duplicate = await PostIngestAsync(scope.Client, envelope with { Correlation = ExternalId("duplicate-external-id") });
        Assert.Equal(first.FaultId, duplicate.FaultId);
        Assert.Equal(first.SignalId, duplicate.SignalId);
        Assert.False(duplicate.IsNewFault);
        Assert.False(duplicate.IsNewJob);

        var acceptedDeliveryCount = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.signals WHERE delivery_key = @delivery_key;",
            ("delivery_key", "external:duplicate-external-id"));
        Assert.Equal(1, acceptedDeliveryCount);

        await ExecuteAsync(
            scope.ConnectionString,
            "UPDATE incidentcompass.faults SET created_at_utc = now() - interval '2 days', completed_at_utc = now() - interval '1 day', status = 'Completed' WHERE id = @id;",
            ("id", first.FaultId));

        var recurrence = await PostIngestAsync(scope.Client, envelope with { Correlation = ExternalId("unique-external-id") });
        Assert.True(recurrence.IsNewJob);

        var neighborCount = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'neighborCount' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", recurrence.JobId!.Value));
        Assert.Equal("2", neighborCount);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_DisallowedSourceKind_ReturnsBadRequest()
    {
        using var scope = await CreateScopeAsync();

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
    public async Task IngestSignal_ConcurrentAttachments_KeepSingleNeighborSetArtifact()
    {
        using var scope = await CreateScopeAsync();
        var envelope = TesterEnvelope(
            "concurrent-neighbor-svc-" + Guid.NewGuid().ToString("N"),
            "prod",
            "TimeoutException",
            "Concurrent neighbor probe timed out",
            "/concurrent-neighbor");

        var first = await PostIngestAsync(scope.Client, envelope);
        var attached = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            PostIngestAsync(
                scope.Client,
                envelope with { Correlation = ExternalId("concurrent-neighbor-" + index) })));

        Assert.All(attached, item =>
        {
            Assert.Equal(first.FaultId, item.FaultId);
            Assert.False(item.IsNewJob);
        });

        var neighborRows = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet' AND attempt IS NULL;",
            ("job_id", first.JobId!.Value));
        Assert.Equal(1, neighborRows);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_RefreshNeighborSet_DoesNotRequirePartialUniqueIndex()
    {
        using var scope = await CreateScopeAsync();
        await ExecuteAsync(
            scope.ConnectionString,
            "DROP INDEX IF EXISTS incidentcompass.ux_triage_artifacts_job_level_kind;");
        try
        {
            var envelope = TesterEnvelope(
                "legacy-neighbor-index-svc-" + Guid.NewGuid().ToString("N"),
                "prod",
                "TimeoutException",
                "Legacy neighbor index probe timed out",
                "/legacy-neighbor-index");

            var first = await PostIngestAsync(scope.Client, envelope);
            var attached = await PostIngestAsync(scope.Client, envelope with { Correlation = ExternalId("legacy-neighbor-index-2") });

            Assert.Equal(first.FaultId, attached.FaultId);
            Assert.False(attached.IsNewJob);

            var neighborRows = await ScalarAsync<long>(
                scope.ConnectionString,
                "SELECT COUNT(*) FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet' AND attempt IS NULL;",
                ("job_id", first.JobId!.Value));
            Assert.Equal(1, neighborRows);

            var neighborCount = await ScalarAsync<string>(
                scope.ConnectionString,
                "SELECT redacted_payload->>'neighborCount' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
                ("job_id", first.JobId.Value));
            Assert.Equal("2", neighborCount);
        }
        finally
        {
            await PostgresSchemaTestHelper.EnsureSchemaAsync(scope.ConnectionString);
        }
    }

    [DockerAvailableFact]
    public async Task IngestSignal_NewFault_MaterializesTriggerSignalAndNeighborSetJobArtifacts()
    {
        using var scope = await CreateScopeAsync();
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
    public async Task IngestSignal_ConcurrentSameFingerprintStrongSignals_SettleOnOneFaultWithoutUniqueViolation()
    {
        using var scope = await CreateScopeAsync();
        var envelope = TesterEnvelope("concurrent-strong-svc", "prod", "TimeoutException", "Concurrent probe timed out", "/concurrent-strong-probe");

        var responses = await Task.WhenAll(
            scope.Client.PostAsJsonAsync("/api/v1/incidents", envelope, TestContext.Current.CancellationToken),
            scope.Client.PostAsJsonAsync("/api/v1/incidents", envelope, TestContext.Current.CancellationToken));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(
            response => response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken)));

        var faultIds = bodies.Select(body => body!.FaultId).Distinct().ToArray();
        Assert.Single(faultIds);

        var jobCount = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_jobs WHERE fault_id = @fault_id;",
            ("fault_id", faultIds[0]));
        Assert.Equal(1, jobCount);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_WeakSignalsWithoutServiceName_EachOpenOwnFaultWithNullIsMassIssue()
    {
        using var scope = await CreateScopeAsync();

        var first = await PostIngestAsync(scope.Client, UserReportEnvelope("Site is down, checkout does nothing (probe A)"));
        var second = await PostIngestAsync(scope.Client, UserReportEnvelope("Site is down, checkout does nothing (probe B)"));

        Assert.NotEqual(first.FaultId, second.FaultId);

        foreach (var ingested in new[] { first, second })
        {
            var faultResponse = await scope.Client.GetAsync($"/api/v1/faults/{ingested.FaultId}", TestContext.Current.CancellationToken);
            var fault = await faultResponse.Content.ReadFromJsonAsync<FaultDetailsResponseDto>(TestContext.Current.CancellationToken);
            Assert.NotNull(fault);
            Assert.Equal("Weak", fault.FingerprintStrength);

            var isMassIssue = await ScalarOrNullAsync<string>(
                scope.ConnectionString,
                "SELECT redacted_payload->>'isMassIssue' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
                ("job_id", ingested.JobId!.Value));
            Assert.Null(isMassIssue);
        }
    }

    [DockerAvailableFact]
    public async Task IngestSignal_TenantIdInRequestBody_IsIgnoredAndDefaultTenantIsStamped()
    {
        using var scope = await CreateScopeAsync();

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
        var ingested = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(ingested);

        var tenantId = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT tenant_id FROM incidentcompass.signals WHERE id = @signal_id;",
            ("signal_id", ingested.SignalId));
        Assert.Equal("local", tenantId);
    }

    [DockerAvailableFact]
    public async Task IngestSignal_NeighborCountClearsThreshold_MarksNeighborSetArtifactAsMassIssue()
    {
        using var scope = await CreateScopeAsync();
        var envelope = TesterEnvelope("mass-issue-probe-svc", "prod", "TimeoutException", "Mass issue probe timed out", "/mass-issue-probe");

        // Every neighbor signal goes through the real ingestion pipeline (normalize/redact/
        // fingerprint via live POSTs), not a hand-seeded SQL row. The first POST opens the fault;
        // the next 5 attach to that same open fault and refresh the existing job-level NeighborSet.
        var first = await PostIngestAsync(scope.Client, envelope);
        for (var index = 0; index < 5; index++)
        {
            var attached = await PostIngestAsync(scope.Client, envelope);
            Assert.Equal(first.FaultId, attached.FaultId);
            Assert.False(attached.IsNewJob);
        }

        var openFaultIsMassIssue = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'isMassIssue' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", first.JobId!.Value));
        Assert.Equal("true", openFaultIsMassIssue);

        var openFaultNeighborCount = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'neighborCount' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", first.JobId.Value));
        Assert.Equal("6", openFaultNeighborCount);

        var openFaultNeighborRows = await ScalarAsync<long>(
            scope.ConnectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", first.JobId.Value));
        Assert.Equal(1, openFaultNeighborRows);

        // Phase 1 has no real "close a fault" mechanism yet (that arrives with Phase 5's report
        // pipeline) -- simulate it directly, backdating well past the configured silence window so
        // the next matching signal opens a recurrence fault+job whose NeighborSet counts all 6 real
        // signals above plus itself. completed_at_utc must stay >= created_at_utc (faults_check), so
        // back-date both together.
        await ExecuteAsync(
            scope.ConnectionString,
            "UPDATE incidentcompass.faults SET created_at_utc = now() - interval '2 days', completed_at_utc = now() - interval '1 day', status = 'Completed' WHERE id = @id;",
            ("id", first.FaultId));

        var recurrence = await PostIngestAsync(scope.Client, envelope);
        Assert.NotEqual(first.FaultId, recurrence.FaultId);
        Assert.True(recurrence.IsNewJob);

        var isMassIssue = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT redacted_payload->>'isMassIssue' FROM incidentcompass.triage_artifacts WHERE job_id = @job_id AND kind = 'NeighborSet';",
            ("job_id", recurrence.JobId!.Value));
        Assert.Equal("true", isMassIssue);

        var recurrenceFaultResponse = await scope.Client.GetAsync($"/api/v1/faults/{recurrence.FaultId}", TestContext.Current.CancellationToken);
        var recurrenceFault = await recurrenceFaultResponse.Content.ReadFromJsonAsync<FaultDetailsResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(recurrenceFault);
        Assert.Equal(first.FaultId, recurrenceFault.RecurrenceOf);
    }

    private async Task<TestScope> CreateScopeAsync(bool useSmallSilenceWindowConfig = false)
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseExplicitMockProviders();
            if (useSmallSilenceWindowConfig)
            {
                builder.UseSetting("IncidentCompass:ConfigSource:Path", TestFixtureConfigPath());
            }
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        return new TestScope(factory, client, connectionString);
    }

    private static string TestFixtureConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-triage-config", "incidentcompass.config.json");

    private static async Task<IngestSignalResponseDto> PostIngestAsync(HttpClient client, object envelope)
    {
        var response = await client.PostAsJsonAsync("/api/v1/incidents", envelope, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }


    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static TesterEnvelopeDto TesterEnvelope(
        string serviceName,
        string environment,
        string errorType,
        string errorMessage,
        string httpRoute,
        DateTimeOffset? observedAtUtc = null)
    {
        return new TesterEnvelopeDto(
            "tester",
            serviceName,
            environment,
            observedAtUtc ?? DateTimeOffset.UtcNow,
            new TesterAttributesDto(errorType, errorMessage, httpRoute));
    }

    private static UserReportEnvelopeDto UserReportEnvelope(string summary) => new("user", summary);

    private static IncidentCorrelationDto ExternalId(string externalId) => new(TraceId: null, SpanId: null, ExternalId: externalId);

    private static async Task<IReadOnlyList<ArtifactRow>> QueryArtifactKindsAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT kind, attempt FROM incidentcompass.triage_artifacts WHERE job_id = @job_id;",
            connection);
        command.Parameters.AddWithValue("job_id", jobId);

        var rows = new List<ArtifactRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ArtifactRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        }

        return rows;
    }

    private static async Task InsertSnapshotAsync(string connectionString, string configHash)
    {
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO incidentcompass.triage_config_snapshots (config_hash, serialized_config, instructions, created_at_utc)
            VALUES (@config_hash, @serialized_config::jsonb, @instructions::jsonb, now())
            ON CONFLICT (config_hash) DO UPDATE
            SET serialized_config = EXCLUDED.serialized_config,
                instructions = EXCLUDED.instructions;
            """,
            ("config_hash", configHash),
            ("serialized_config", """
                {
                  "Providers": {
                    "local-oai": { "Kind": "OpenAICompatible", "Endpoint": "http://localhost:1234/v1", "ApiKeySecretRef": "LOCAL_OAI_KEY" }
                  },
                  "Routes": {
                    "analysis-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "snapshot-analysis-model", "Temperature": 0.1, "MaxOutputTokens": 2000, "ContextWindowTokens": 8192 },
                    "report-chat": { "Kind": "Chat", "ProviderId": "local-oai", "Model": "snapshot-report-model", "Temperature": 0.2, "MaxOutputTokens": 4000, "ContextWindowTokens": 8192 },
                    "memory-embed": { "Kind": "Embedding", "ProviderId": "local-oai", "Model": "snapshot-embedding-model" }
                  },
                  "Orchestrator": {
                    "Instructions": "ref:instructions/orchestrator.md",
                    "RouteId": "report-chat",
                    "Tools": ["delegate", "publish_report"],
                    "Budget": { "MaxWorkers": 6, "MaxTokens": 200000, "MaxWallClockSeconds": 120 }
                  },
                  "Roles": {
                    "analysis": { "RouteId": "analysis-chat", "Instructions": "ref:instructions/analysis.md", "Tools": [], "OutputSchema": "ref:schemas/analysis.json" }
                  },
                  "Tools": {},
                  "Rules": [
                    { "Type": "rate_cap", "Tool": "*", "Max": 50 }
                  ],
                  "Ingestion": { "DefaultTenant": "local", "AllowedSources": ["otel", "user", "tester", "manual"] },
                  "FaultGrouping": {
                    "LookbackMinutes": 15,
                    "SilenceWindowMinutes": 30,
                    "FingerprintVersion": 1,
                    "MassIssue": { "MinNeighborCount": 5, "MinFingerprintStrength": "strong" }
                  }
                }
                """),
            ("instructions", """
                {
                  "ref:instructions/orchestrator.md": "snapshot orchestrator instructions",
                  "ref:instructions/analysis.md": "snapshot analysis instructions",
                  "ref:schemas/analysis.json": "{ \"type\": \"object\", \"additionalProperties\": false }"
                }
                """));
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

        var result = await command.ExecuteScalarAsync();
        return (T)result!;
    }

    private static async Task<T?> ScalarOrNullAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
        where T : class
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (T)result;
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

    private sealed record ArtifactRow(string Kind, int? Attempt);

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
        TesterAttributesDto Attributes,
        IncidentCorrelationDto? Correlation = null);

    private sealed record IncidentCorrelationDto(string? TraceId, string? SpanId, string? ExternalId);

    private sealed record UserReportEnvelopeDto(string SourceKind, string Summary);

    private sealed record IngestSignalResponseDto(
        Guid SignalId,
        Guid FaultId,
        bool IsNewFault,
        bool IsNewJob,
        bool IsSuppressed,
        Guid? JobId,
        string? ConfigHash);

    private sealed record TriageJobSummaryDto(Guid Id, string Status, int Attempt, string ConfigHash, DateTimeOffset CreatedAtUtc);

    private sealed record FaultDetailsResponseDto(
        Guid Id,
        string Status,
        string Fingerprint,
        int FingerprintVersion,
        string FingerprintStrength,
        bool CanGroup,
        string ServiceName,
        string Environment,
        string? Severity,
        string? CorrelationId,
        Guid TriggerSignalId,
        Guid? RecurrenceOf,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        TriageJobSummaryDto? Job);
}
