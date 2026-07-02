using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Investigation.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TriageInvestigationRealLlmSmokeTests(PostgresRepositoryFixture postgres)
{
    [RealLocalLlmSmokeFact]
    public async Task RealLocalModel_ReachesPublishReportAtMeasuredRate()
    {
        var settings = RealLocalLlmSmokeSettings.FromEnvironment();
        var configPath = await CreateSmokeConfigurationAsync(settings.Model);
        using var scope = await CreateScopeAsync(configPath, settings);
        var outcomes = new List<SmokeOutcome>();

        for (var index = 1; index <= settings.Runs; index++)
        {
            outcomes.Add(await RunOneAsync(scope, index, settings));
        }

        await WriteResultAsync(settings, outcomes);
    }

    private async Task<SmokeOutcome> RunOneAsync(
        TestScope scope,
        int index,
        RealLocalLlmSmokeSettings settings)
    {
        try
        {
            var ingested = await PostIngestAsync(scope.Client, TesterEnvelope(index));
            if (ingested.JobId is null)
            {
                return new SmokeOutcome(index, false, "ingest_did_not_create_job");
            }

            using var serviceScope = scope.Factory.Services.CreateScope();
            var runner = serviceScope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
            var claimed = await runner.ClaimNextAsync(
                "worker-real-llm-smoke",
                TimeSpan.FromSeconds(settings.LeaseSeconds),
                TestContext.Current.CancellationToken);
            if (claimed is null)
            {
                return new SmokeOutcome(index, false, "worker_did_not_claim_job");
            }

            await runner.ProcessClaimedAsync(
                claimed,
                "worker-real-llm-smoke",
                new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken);

            var job = await ReadJobAsync(scope.ConnectionString, claimed.Id);
            var report = await ReadReportOrNullAsync(scope.ConnectionString, ingested.FaultId);
            var reached = job.Status == "Succeeded" && report is not null;
            var detail = reached
                ? "publish_report_reached"
                : job.Status + FormatFailure(job.LastErrorCode, job.LastErrorMessage);
            return new SmokeOutcome(index, reached, detail);
        }
        catch (Exception exception)
        {
            return new SmokeOutcome(index, false, exception.GetType().Name + ": " + exception.Message);
        }
    }

    private async Task<TestScope> CreateScopeAsync(
        string configPath,
        RealLocalLlmSmokeSettings settings)
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:IncidentCompass", connectionString);
            builder.UseSetting("IncidentCompass:ConfigSource:Path", configPath);
            builder.UseSetting("IncidentCompass:ModelGateway:Provider", "OpenAiCompatible");
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:BaseUrl", settings.BaseUrl);
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:ChatCompletionsPath", settings.ChatCompletionsPath);
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:ApiKey", settings.ApiKey);
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:AllowInsecureHttpForLoopback", "true");
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:TimeoutSeconds", settings.TimeoutSeconds.ToString());
            builder.UseSetting("IncidentCompass:ModelGateway:OpenAiCompatible:MaxRetryAttempts", "0");
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        return new TestScope(factory, client, connectionString);
    }

    private static async Task<string> CreateSmokeConfigurationAsync(string model)
    {
        var directory = Path.Combine(Path.GetTempPath(), "incidentcompass-real-llm-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "instructions"));
        Directory.CreateDirectory(Path.Combine(directory, "schemas"));

        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "orchestrator.md"), """
            You are the IncidentCompass Phase 3 smoke orchestrator. /no_think You have only delegate and publish_report.
            First call delegate with role analysis and a short task. After the tool result, call publish_report with report_json.
            The report_json status must be Completed or InsufficientEvidence, summary must be non-empty, classification must be one of KnownIncident, LikelyRegression, SimpleKnownError, Unknown, or Noise, and confidence must be Low, Medium, or High.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "analysis.md"), """
            You are the analysis worker. /no_think Return only JSON with keyFacts, candidateClassification, needsDeeperContext, and optional rationale. keyFacts must be an array of plain strings, never objects.
            Use SimpleKnownError when the trigger signal describes a timeout; use Unknown when there is not enough evidence.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "schemas", "analysis.json"), """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "keyFacts": { "type": "array", "items": { "type": "string" } },
                "candidateClassification": { "type": "string", "enum": ["KnownIncident", "LikelyRegression", "SimpleKnownError", "Unknown", "Noise"] },
                "needsDeeperContext": { "type": "boolean" },
                "rationale": { "type": "string" }
              },
              "required": ["keyFacts", "candidateClassification", "needsDeeperContext"]
            }
            """);

        var config = new JsonObject
        {
            ["Providers"] = new JsonObject
            {
                ["local-oai"] = new JsonObject
                {
                    ["Kind"] = "OpenAICompatible",
                    ["Endpoint"] = "local-smoke",
                    ["ApiKeySecretRef"] = "INCIDENTCOMPASS_REAL_LLM_SMOKE_API_KEY"
                }
            },
            ["Routes"] = new JsonObject
            {
                ["analysis-chat"] = ChatRoute(model, 0.1, 4096),
                ["report-chat"] = ChatRoute(model, 0.1, 8192),
                ["memory-embed"] = new JsonObject
                {
                    ["Kind"] = "Embedding",
                    ["ProviderId"] = "local-oai",
                    ["Model"] = "unused-smoke-embedding"
                }
            },
            ["Orchestrator"] = new JsonObject
            {
                ["Instructions"] = "ref:instructions/orchestrator.md",
                ["RouteId"] = "report-chat",
                ["Tools"] = new JsonArray("delegate", "publish_report"),
                ["Budget"] = new JsonObject
                {
                    ["MaxWorkers"] = 1,
                    ["MaxTokens"] = 12000,
                    ["MaxWallClockSeconds"] = 180,
                    ["MaxReprompts"] = 2
                }
            },
            ["Roles"] = new JsonObject
            {
                ["analysis"] = new JsonObject
                {
                    ["RouteId"] = "analysis-chat",
                    ["Instructions"] = "ref:instructions/analysis.md",
                    ["Tools"] = new JsonArray(),
                    ["OutputSchema"] = "ref:schemas/analysis.json"
                }
            },
            ["Tools"] = new JsonObject(),
            ["Rules"] = new JsonArray(),
            ["Ingestion"] = new JsonObject
            {
                ["DefaultTenant"] = "local",
                ["AllowedSources"] = new JsonArray("otel", "user", "tester", "manual")
            },
            ["FaultGrouping"] = new JsonObject
            {
                ["LookbackMinutes"] = 15,
                ["SilenceWindowMinutes"] = 30,
                ["FingerprintVersion"] = 1,
                ["MassIssue"] = new JsonObject
                {
                    ["MinNeighborCount"] = 5,
                    ["MinFingerprintStrength"] = "strong"
                }
            }
        };

        var configPath = Path.Combine(directory, "incidentcompass.config.json");
        await File.WriteAllTextAsync(configPath, config.ToJsonString());
        return configPath;
    }

    private static JsonObject ChatRoute(string model, double temperature, int maxOutputTokens)
    {
        return new JsonObject
        {
            ["Kind"] = "Chat",
            ["ProviderId"] = "local-oai",
            ["Model"] = model,
            ["Temperature"] = temperature,
            ["MaxOutputTokens"] = maxOutputTokens,
            ["ContextWindowTokens"] = 8192
        };
    }

    private static async Task<IngestSignalResponseDto> PostIngestAsync(HttpClient client, object envelope)
    {
        var response = await client.PostAsJsonAsync("/api/v1/incidents", envelope, TestContext.Current.CancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            return new IngestSignalResponseDto(Guid.Empty, Guid.Empty, false, false, false, null, null);
        }

        var body = await response.Content.ReadFromJsonAsync<IngestSignalResponseDto>(TestContext.Current.CancellationToken);
        return body!;
    }

    private static TesterEnvelopeDto TesterEnvelope(int index)
    {
        var unique = Guid.NewGuid().ToString("N");
        return new TesterEnvelopeDto(
            "tester",
            "real-llm-smoke-payments-" + unique,
            "prod",
            DateTimeOffset.UtcNow,
            new TesterAttributesDto("TimeoutException", "Smoke run " + index + " checkout timeout " + unique, "/checkout"));
    }

    private static async Task<JobRow> ReadJobAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status, last_error_code, last_error_message FROM incidentcompass.triage_jobs WHERE id = @job_id;", connection);
        command.Parameters.AddWithValue("job_id", jobId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new JobRow("missing", null, null);
        }

        return new JobRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<ReportRow?> ReadReportOrNullAsync(string connectionString, Guid faultId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM incidentcompass.triage_reports WHERE fault_id = @fault_id;", connection);
        command.Parameters.AddWithValue("fault_id", faultId);
        var status = await command.ExecuteScalarAsync();
        return status is null ? null : new ReportRow(status.ToString()!);
    }

    private static async Task WriteResultAsync(
        RealLocalLlmSmokeSettings settings,
        IReadOnlyCollection<SmokeOutcome> outcomes)
    {
        var reached = outcomes.Count(static outcome => outcome.ReachedPublishReport);
        var lines = new List<string>
        {
            "# Phase 3 Real Local LLM Smoke Result",
            "",
            "- GeneratedUtc: " + DateTimeOffset.UtcNow.ToString("O"),
            "- Endpoint: " + settings.BaseUrl,
            "- ChatCompletionsPath: " + settings.ChatCompletionsPath,
            "- Model: " + settings.Model,
            "- Runs: " + outcomes.Count,
            "- publish_report reach-rate: " + reached + "/" + outcomes.Count,
            "",
            "## Outcomes"
        };

        lines.AddRange(outcomes.Select(static outcome =>
            "- Run " + outcome.RunIndex + ": " + (outcome.ReachedPublishReport ? "reached" : "missed") + " — " + outcome.Detail));
        Directory.CreateDirectory(Path.GetDirectoryName(settings.ResultPath)!);
        await File.WriteAllLinesAsync(settings.ResultPath, lines);
    }

    private sealed record TestScope(WebApplicationFactory<Program> Factory, HttpClient Client, string ConnectionString) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed record SmokeOutcome(int RunIndex, bool ReachedPublishReport, string Detail);

    private static string FormatFailure(string? code, string? message)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return " (" + code + ": " + message + ")";
    }

    private sealed record JobRow(string Status, string? LastErrorCode, string? LastErrorMessage);

    private sealed record ReportRow(string Status);

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
}

public sealed class RealLocalLlmSmokeFactAttribute : FactAttribute
{
    public RealLocalLlmSmokeFactAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IsTruthy(Environment.GetEnvironmentVariable("INCIDENTCOMPASS_REAL_LLM_SMOKE")))
        {
            Skip = "Set INCIDENTCOMPASS_REAL_LLM_SMOKE=true to run the non-gated real local LLM smoke.";
        }
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
            (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record RealLocalLlmSmokeSettings(
    string BaseUrl,
    string ChatCompletionsPath,
    string Model,
    string ApiKey,
    int Runs,
    int LeaseSeconds,
    int TimeoutSeconds,
    string ResultPath)
{
    public static RealLocalLlmSmokeSettings FromEnvironment()
    {
        return new RealLocalLlmSmokeSettings(
            Read("INCIDENTCOMPASS_REAL_LLM_SMOKE_BASE_URL", "http://localhost:1234"),
            Read("INCIDENTCOMPASS_REAL_LLM_SMOKE_CHAT_PATH", "/v1/chat/completions"),
            Read("INCIDENTCOMPASS_REAL_LLM_SMOKE_MODEL", "local-model"),
            Read("INCIDENTCOMPASS_REAL_LLM_SMOKE_API_KEY", "local-smoke-key"),
            ReadPositiveInt("INCIDENTCOMPASS_REAL_LLM_SMOKE_RUNS", 3),
            ReadPositiveInt("INCIDENTCOMPASS_REAL_LLM_SMOKE_LEASE_SECONDS", 180),
            ReadPositiveInt("INCIDENTCOMPASS_REAL_LLM_SMOKE_TIMEOUT_SECONDS", 120),
            Path.GetFullPath(Read("INCIDENTCOMPASS_REAL_LLM_SMOKE_RESULT_PATH", Path.Combine("docs", "phase-2-real-llm-smoke-result.md"))));
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadPositiveInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
