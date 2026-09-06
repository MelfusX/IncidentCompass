using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

internal static class IncidentIngestionTestSupport
{
    public static async Task<IngestionTestScope> CreateScopeAsync(
        PostgresRepositoryFixture postgres,
        bool useSmallSilenceWindowConfig = false)
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

        return new IngestionTestScope(factory, client, connectionString);
    }

    public static string TestFixtureConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-triage-config", "incidentcompass.config.json");

    public static Task CompleteOutsideSilenceWindowAsync(string connectionString, Guid faultId) =>
        ExecuteAsync(
            connectionString,
            "UPDATE incidentcompass.faults SET created_at_utc = now() - interval '20 minutes', completed_at_utc = now() - interval '10 minutes', status = 'Completed' WHERE id = @id;",
            ("id", faultId));
    public static async Task<IngestionSignalResponse> PostIngestAsync(HttpClient client, object envelope)
    {
        var response = await client.PostAsJsonAsync("/api/v1/incidents", envelope, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestionSignalResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }


    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }

    public static IngestionTesterEnvelope TesterEnvelope(
        string serviceName,
        string environment,
        string errorType,
        string errorMessage,
        string httpRoute,
        DateTimeOffset? observedAtUtc = null)
    {
        return new IngestionTesterEnvelope(
            "tester",
            serviceName,
            environment,
            observedAtUtc ?? DateTimeOffset.UtcNow,
            new IngestionTesterAttributes(errorType, errorMessage, httpRoute));
    }

    public static IngestionUserReportEnvelope UserReportEnvelope(string summary) => new("user", summary);

    public static IngestionCorrelation ExternalId(string externalId) => new(TraceId: null, SpanId: null, ExternalId: externalId);

    public static async Task<IReadOnlyList<IngestionArtifactRow>> QueryArtifactKindsAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT kind, attempt FROM incidentcompass.triage_artifacts WHERE job_id = @job_id;",
            connection);
        command.Parameters.AddWithValue("job_id", jobId);

        var rows = new List<IngestionArtifactRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new IngestionArtifactRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        }

        return rows;
    }

    public static async Task InsertSnapshotAsync(string connectionString, string configHash)
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

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
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

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
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

    public static async Task<T?> ScalarOrNullAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
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
}
