using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class ApiKeyTenantIsolationTests(PostgresRepositoryFixture postgres)
{
    private const string KeyA = "api_key_A_tenant_isolation_sentinel_123456789";
    private const string KeyB = "api_key_B_tenant_isolation_sentinel_123456789";

    [DockerAvailableFact]
    public async Task ManualAndOtlpIngestUseKeyTenantAndKeepSecretsOutOfDurableState()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var suffix = Guid.NewGuid().ToString("N");
        var manualService = "api-key-manual-" + suffix;
        var otlpService = "api-key-otlp-" + suffix;

        using var manualRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incidents")
        {
            Content = JsonContent.Create(new
            {
                sourceKind = "tester",
                serviceName = manualService,
                environment = "test",
                observedAtUtc = DateTimeOffset.UtcNow,
                tenantId = "tenant-b",
                attributes = new
                {
                    errorType = "TimeoutException",
                    errorMessage = "API-key tenant isolation probe",
                    httpRoute = "/api-key-probe"
                }
            })
        };
        AddKey(manualRequest, KeyA);
        manualRequest.Headers.Add("X-Demo-Tenant-Id", "tenant-b");
        var manualResponse = await client.SendAsync(manualRequest, TestContext.Current.CancellationToken);
        var manual = await manualResponse.Content.ReadFromJsonAsync<IngestResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, manualResponse.StatusCode);
        Assert.NotNull(manual);
        Assert.Equal("tenant-a", await ScalarAsync<string>(
            connectionString,
            "SELECT tenant_id FROM incidentcompass.signals WHERE id = @id;",
            ("id", manual.SignalId)));
        Assert.NotNull(manual.JobId);
        var reportId = await SeedReportAndLedgerAsync(
            connectionString,
            manual.FaultId,
            manual.JobId.Value,
            manual.ConfigHash);
        await AssertTenantReadMatrixAsync(client, manual.FaultId, reportId);

        using var otlpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/traces")
        {
            Content = new ByteArrayContent(CreateTraceExport(otlpService).ToByteArray())
        };
        otlpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        AddKey(otlpRequest, KeyB);
        var otlpResponse = await client.SendAsync(otlpRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, otlpResponse.StatusCode);
        Assert.Equal("tenant-b", await ScalarAsync<string>(
            connectionString,
            "SELECT tenant_id FROM incidentcompass.signals WHERE service_name = @service_name;",
            ("service_name", otlpService)));
        var otlpFaultId = await ScalarAsync<Guid>(
            connectionString,
            "SELECT fault_id FROM incidentcompass.signals WHERE service_name = @service_name;",
            ("service_name", otlpService));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, $"/api/v1/faults/{otlpFaultId}", KeyA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, $"/api/v1/faults/{otlpFaultId}", KeyB)).StatusCode);

        await AssertSecretAbsentAsync(connectionString, KeyA);
        await AssertSecretAbsentAsync(connectionString, Digest(KeyA));
        await AssertSecretAbsentAsync(connectionString, KeyB);
        await AssertSecretAbsentAsync(connectionString, Digest(KeyB));
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(static logging => logging.ClearProviders());
            builder.UseExplicitMockProviders();
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:PermitLimit", "100");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:WindowSeconds", "300");
            SetCredential(builder, 0, "key-a", "tenant-a", KeyA);
            SetCredential(builder, 1, "key-b", "tenant-b", KeyB);
        });

    private static ExportTraceServiceRequest CreateTraceExport(string serviceName) =>
        new()
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes =
                        {
                            Attribute("service.name", serviceName),
                            Attribute("deployment.environment.name", "test"),
                            Attribute("tenant.id", "tenant-a")
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
                                    Name = "GET /api-key-otlp",
                                    TraceId = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()),
                                    SpanId = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()[..8]),
                                    StartTimeUnixNano = 1_700_000_000_000_000_000,
                                    EndTimeUnixNano = 1_700_000_000_100_000_000,
                                    Status = new Status { Code = Status.Types.StatusCode.Error },
                                    Attributes =
                                    {
                                        Attribute("exception.type", "TimeoutException"),
                                        Attribute("exception.message", "API-key OTLP tenant probe")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

    private static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static void SetCredential(IWebHostBuilder builder, int index, string keyId, string tenantId, string key)
    {
        var prefix = $"IncidentCompass:ApiKeyAuth:Credentials:{index}";
        builder.UseSetting($"{prefix}:KeyId", keyId);
        builder.UseSetting($"{prefix}:TenantId", tenantId);
        builder.UseSetting($"{prefix}:Sha256Digest", Digest(key));
    }

    private static void AddKey(HttpRequestMessage request, string key) =>
        request.Headers.Add("X-IncidentCompass-Key", key);

    private static async Task AssertTenantReadMatrixAsync(HttpClient client, Guid faultId, Guid reportId)
    {
        foreach (var route in new[]
        {
            $"/api/v1/faults/{faultId}",
            $"/api/v1/faults/{faultId}/ledger",
            $"/api/v1/triage-reports/{reportId}",
            $"/api/v1/faults/{faultId}/triage-report"
        })
        {
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, route, KeyA)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, route, KeyB)).StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string route, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        AddKey(request, key);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> SeedReportAndLedgerAsync(
        string connectionString,
        Guid faultId,
        Guid jobId,
        string configHash)
    {
        var reportId = Guid.NewGuid();
        await ExecuteAsync(
            connectionString,
            """
            UPDATE incidentcompass.faults
            SET status = 'Completed', completed_at_utc = now()
            WHERE id = @fault_id;

            UPDATE incidentcompass.triage_jobs
            SET status = 'Succeeded', updated_at_utc = now()
            WHERE id = @job_id;

            INSERT INTO incidentcompass.triage_reports (
                id, job_id, fault_id, status, summary, classification, confidence,
                documentation_fit, limitations, config_hash, created_at_utc)
            VALUES (
                @report_id, @job_id, @fault_id, 'Completed', 'API-key tenant isolation report.',
                'KnownIncident', 'High', 'Missing', ARRAY[]::text[], @config_hash, now());

            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, tool_name, rationale,
                payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, 1, 'ReportPublished', 'publish_report',
                'API-key tenant isolation report.', @payload_ref, @config_hash, now());
            """,
            ("fault_id", faultId),
            ("job_id", jobId),
            ("report_id", reportId),
            ("payload_ref", "report:" + reportId),
            ("config_hash", configHash));

        Assert.Equal(1L, await ScalarAsync<long>(
            connectionString,
            "SELECT COUNT(*) FROM incidentcompass.triage_ledger WHERE job_id = @job_id AND event_type = 'ReportPublished';",
            ("job_id", jobId)));
        return reportId;
    }

    private static async Task AssertSecretAbsentAsync(string connectionString, string secret)
    {
        Assert.Equal(0L, await ScalarAsync<long>(
            connectionString,
            """
            SELECT
                (SELECT COUNT(*) FROM incidentcompass.triage_config_snapshots
                    WHERE position(@secret in serialized_config::text) > 0
                       OR position(@secret in instructions::text) > 0) +
                (SELECT COUNT(*) FROM incidentcompass.triage_ledger WHERE position(@secret in coalesce(rationale, '')) > 0);
            """,
            ("secret", secret)));
    }

    private static async Task<T> ScalarAsync<T>(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    private sealed record IngestResponse(Guid SignalId, Guid FaultId, Guid? JobId, string ConfigHash);
}
