using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Memory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class TriageInvestigationRealLlmSmokeTests(PostgresRepositoryFixture postgres)
{
    private const string MemoryModel = "mock-memory-embedding-v1";

    [RealLocalLlmSmokeFact]
    public async Task RealLocalModel_ReachesPublishReportAtMeasuredRate()
    {
        var settings = RealLocalLlmSmokeSettings.FromEnvironment();
        var endpoint = await ProbeEndpointAsync(settings);
        if (!endpoint.IsReachable)
        {
            await WriteNotExecutedResultAsync(settings, endpoint.Detail);
            return;
        }

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
            await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(scope.ConnectionString);
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

            if (claimed.Id != ingested.JobId.Value)
            {
                return new SmokeOutcome(index, false, "worker_claimed_unexpected_job_id " + claimed.Id);
            }

            await runner.ProcessClaimedAsync(
                claimed,
                "worker-real-llm-smoke",
                new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken);

            var job = await ReadJobAsync(scope.ConnectionString, claimed.Id);
            var report = await ReadReportOrNullAsync(scope.ConnectionString, ingested.FaultId);
            var reportHasEvidence = report is not null && await DidReportHaveResolvingEvidenceAsync(scope.ConnectionString, ingested.FaultId);
            var memoryWorkerReached = await DidMemoryWorkerProduceOutputAsync(scope.ConnectionString, claimed.Id);
            var memorySearchSucceeded = await DidMemorySearchSucceedAsync(scope.ConnectionString, claimed.Id);
            var reached = job.Status == "Succeeded" && report is not null && reportHasEvidence && memoryWorkerReached && memorySearchSucceeded;
            var detail = reached
                ? "delegate_memory_memory_search_publish_report_with_evidence_reached"
                : job.Status + FormatFailure(job.LastErrorCode, job.LastErrorMessage) +
                  FormatTrajectoryFailure(report is not null, reportHasEvidence, memoryWorkerReached, memorySearchSucceeded);
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
        await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(connectionString);
        await ClearMemoryAsync(connectionString);

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
            builder.UseSetting("IncidentCompass:Embeddings:Provider", "Mock");
            builder.UseSetting("IncidentCompass:Embeddings:DefaultModel", MemoryModel);
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await SeedCheckoutRunbookAsync(factory);

        return new TestScope(factory, client, connectionString);
    }

    private static async Task<string> CreateSmokeConfigurationAsync(string model)
    {
        var directory = Path.Combine(Path.GetTempPath(), "incidentcompass-real-llm-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "instructions"));
        Directory.CreateDirectory(Path.Combine(directory, "schemas"));

        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "orchestrator.md"), """
            You are the IncidentCompass Phase 5 smoke orchestrator. /no_think You have only delegate and publish_report.
            First call delegate with role analysis and a short task. After the analysis result, call delegate with role memory and ask it to search for checkout timeout runbook context.
            Do not call publish_report until the memory worker has returned. Then call publish_report with report_json.
            The report_json status must be Completed or InsufficientEvidence, summary must be non-empty, classification must be one of KnownIncident, LikelyRegression, SimpleKnownError, Unknown, or Noise, and confidence must be Low, Medium, or High.
            The report_json evidence array must cite citable artifactId values exactly. Include a trigger or neighbor artifact from the job context and include a memory artifactId from the memory worker items when memory matched. Copy quote text exactly from the cited artifact text or omit the quote.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "analysis.md"), """
            You are the analysis worker. /no_think Return only JSON with keyFacts, candidateClassification, needsDeeperContext, and optional rationale. keyFacts must be an array of plain strings, never objects.
            Use SimpleKnownError when the trigger signal describes a timeout. Set needsDeeperContext to true for checkout timeout, inventory timeout, or unknown cases.
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "instructions", "memory.md"), """
            You are the memory worker. /no_think First call memory_search with a concise query about checkout TimeoutException, /checkout, and inventory latency.
            After memory_search returns, return only JSON matching your schema. Copy artifactId values exactly from memory_search result items. Use matched false and an honest noMatchReason when memory_search returns no items.
            The artifactId belongs inside each items[] element, never at the top level.
            Example: {"matched": true, "items": [{"artifactId": "<from memory_search result>", "title": "Checkout Timeout Runbook", "quote": "Checkout requests time out while waiting on inventory.", "score": 0.87}], "noMatchReason": null}
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
        await File.WriteAllTextAsync(
            Path.Combine(directory, "schemas", "memory.json"),
            await File.ReadAllTextAsync(
                Path.Combine(FindRepositoryRoot(), "config", "schemas", "memory.json"),
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        var config = new JsonObject
        {
            ["Providers"] = new JsonObject
            {
                ["local-oai"] = new JsonObject
                {
                    ["Kind"] = "OpenAICompatible",
                    ["Endpoint"] = "local-smoke",
                    ["ApiKeySecretRef"] = "INCIDENTCOMPASS_LLM_SMOKE_API_KEY"
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
                    ["Model"] = MemoryModel
                }
            },
            ["Orchestrator"] = new JsonObject
            {
                ["Instructions"] = "ref:instructions/orchestrator.md",
                ["RouteId"] = "report-chat",
                ["Tools"] = new JsonArray("delegate", "publish_report"),
                ["Budget"] = new JsonObject
                {
                    ["MaxWorkers"] = 2,
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
                },
                ["memory"] = new JsonObject
                {
                    ["RouteId"] = "analysis-chat",
                    ["Instructions"] = "ref:instructions/memory.md",
                    ["Tools"] = new JsonArray("memory_search"),
                    ["OutputSchema"] = "ref:schemas/memory.json"
                }
            },
            ["Tools"] = new JsonObject
            {
                ["memory_search"] = new JsonObject
                {
                    ["Kind"] = "internal",
                    ["EmbeddingRouteId"] = "memory-embed",
                    ["TopK"] = 5,
                    ["MinScore"] = 0.25
                }
            },
            ["Rules"] = new JsonArray(new JsonObject
            {
                ["Type"] = "rate_cap",
                ["Tool"] = "*",
                ["Scope"] = "attempt",
                ["Max"] = 50
            }),
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
        await File.WriteAllTextAsync(configPath, config.ToJsonString(), TestContext.Current.CancellationToken);
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

    private static async Task<bool> DidReportHaveResolvingEvidenceAsync(string connectionString, Guid faultId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.triage_reports r
                JOIN incidentcompass.triage_evidence e ON e.report_id = r.id
                WHERE r.fault_id = @fault_id
                  AND e.kind IN ('TriggerSignal', 'NeighborSet', 'PriorReport', 'RetrievedItem', 'Runbook', 'KnownIncident', 'ToolResult'));
            """, connection);
        command.Parameters.AddWithValue("fault_id", faultId);
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> DidMemoryWorkerProduceOutputAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.triage_artifacts
                WHERE job_id = @job_id
                  AND kind = 'WorkerOutput'
                  AND domain_ref = 'worker:memory');
            """, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> DidMemorySearchSucceedAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.triage_ledger
                WHERE job_id = @job_id
                  AND event_type = 'ToolResult'
                  AND tool_name = 'memory_search'
                  AND tool_status = 'Succeeded');
            """, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task ClearMemoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM incidentcompass.memory_items;", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedCheckoutRunbookAsync(WebApplicationFactory<Program> factory)
    {
        using var serviceScope = factory.Services.CreateScope();
        var embeddingClient = serviceScope.ServiceProvider.GetRequiredService<IEmbeddingClient>();
        var repository = serviceScope.ServiceProvider.GetRequiredService<IMemoryRepository>();
        var content = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "samples", "runbooks", "checkout-timeout.md"),
            TestContext.Current.CancellationToken);
        var embedding = await embeddingClient.CreateEmbeddingAsync(
            new EmbeddingRequest(content, MemoryModel, "real-llm-smoke-memory-seed"),
            TestContext.Current.CancellationToken);
        var item = new MemorySeedItem(
            Guid.NewGuid(),
            "local",
            "runbook",
            "samples/runbooks/checkout-timeout.md",
            "Checkout Timeout Runbook",
            content,
            ComputeSha256Hex(content),
            Version: 1,
            ["checkout", "timeout"],
            ServiceName: "checkout",
            Component: null,
            ReleaseName: null);
        var chunk = new MemorySeedChunk(
            Guid.NewGuid(),
            Position: 0,
            content,
            ComputeSha256Hex(content),
            embedding.Provider,
            embedding.Model,
            embedding.Vector.Count,
            embedding.Vector);

        await repository.ReconcileSeedCorpusAsync(
            new MemorySeedCorpus(
                "local",
                "test",
                Guid.NewGuid(),
                new HashSet<string>(StringComparer.Ordinal) { "samples" },
                [new MemorySeedEntry(item, [chunk])]),
            TestContext.Current.CancellationToken);
    }

    private static async Task<EndpointProbeResult> ProbeEndpointAsync(RealLocalLlmSmokeSettings settings)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Min(settings.TimeoutSeconds, 5))
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            using var response = await client.GetAsync(
                CreateModelsEndpointUri(settings),
                TestContext.Current.CancellationToken);
            return new EndpointProbeResult(true, "models_probe_http_" + (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new EndpointProbeResult(false, "endpoint_unreachable (" + exception.GetType().Name + ")");
        }
    }

    private static Uri CreateModelsEndpointUri(RealLocalLlmSmokeSettings settings)
    {
        const string chatSuffix = "/chat/completions";
        var modelPath = settings.ChatCompletionsPath.EndsWith(chatSuffix, StringComparison.Ordinal)
            ? settings.ChatCompletionsPath[..^chatSuffix.Length] + "/models"
            : "/v1/models";
        return new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), modelPath.TrimStart('/'));
    }

    private static async Task WriteNotExecutedResultAsync(
        RealLocalLlmSmokeSettings settings,
        string reason)
    {
        var lines = new List<string>
        {
            "# Phase 5 Real Local LLM Grounded Report Smoke Result",
            "",
            "- GeneratedUtc: " + DateTimeOffset.UtcNow.ToString("O"),
            "- Status: not executed",
            "- Reason: " + reason,
            "- Endpoint: " + CreateModelsEndpointUri(settings),
            "- ChatCompletionsPath: " + settings.ChatCompletionsPath,
            "- Model: " + settings.Model,
            "- Scenario: delegate -> memory -> memory_search -> publish_report -> grounded evidence"
        };

        Directory.CreateDirectory(Path.GetDirectoryName(settings.ResultPath)!);
        await File.WriteAllLinesAsync(settings.ResultPath, lines, TestContext.Current.CancellationToken);
    }

    private static async Task WriteResultAsync(
        RealLocalLlmSmokeSettings settings,
        IReadOnlyCollection<SmokeOutcome> outcomes)
    {
        var reached = outcomes.Count(static outcome => outcome.ReachedGroundedReport);
        var lines = new List<string>
        {
            "# Phase 5 Real Local LLM Grounded Report Smoke Result",
            "",
            "- GeneratedUtc: " + DateTimeOffset.UtcNow.ToString("O"),
            "- Status: executed",
            "- Endpoint: " + settings.BaseUrl,
            "- ModelsEndpoint: " + CreateModelsEndpointUri(settings),
            "- ChatCompletionsPath: " + settings.ChatCompletionsPath,
            "- Model: " + settings.Model,
            "- Runs: " + outcomes.Count,
            "- Scenario: delegate -> memory -> memory_search -> publish_report -> grounded evidence",
            "- full trajectory reach-rate: " + reached + "/" + outcomes.Count,
            "",
            "## Outcomes"
        };

        lines.AddRange(outcomes.Select(static outcome =>
            "- Run " + outcome.RunIndex + ": " + (outcome.ReachedGroundedReport ? "reached" : "missed") + " - " + outcome.Detail));
        Directory.CreateDirectory(Path.GetDirectoryName(settings.ResultPath)!);
        await File.WriteAllLinesAsync(settings.ResultPath, lines, TestContext.Current.CancellationToken);
    }

    private sealed record TestScope(WebApplicationFactory<Program> Factory, HttpClient Client, string ConnectionString) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed record SmokeOutcome(int RunIndex, bool ReachedGroundedReport, string Detail);

    private sealed record EndpointProbeResult(bool IsReachable, string Detail);

    private static string FormatFailure(string? code, string? message)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return " (" + code + ": " + message + ")";
    }

    private static string FormatTrajectoryFailure(
        bool reportWritten,
        bool reportHasEvidence,
        bool memoryWorkerReached,
        bool memorySearchSucceeded)
    {
        return " trajectory(report=" + reportWritten +
               ", evidence=" + reportHasEvidence +
               ", memory_worker=" + memoryWorkerReached +
               ", memory_search=" + memorySearchSucceeded + ")";
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new FileInfo(sourceFilePath).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
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
        if (!IsTruthy(Environment.GetEnvironmentVariable("INCIDENTCOMPASS_LLM_SMOKE_ENABLED")))
        {
            Skip = "Set INCIDENTCOMPASS_LLM_SMOKE_ENABLED=true to run the non-gated real local LLM smoke.";
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
            Read("INCIDENTCOMPASS_LLM_SMOKE_BASE_URL", "http://localhost:1234"),
            Read("INCIDENTCOMPASS_LLM_SMOKE_CHAT_PATH", "/v1/chat/completions"),
            Read("INCIDENTCOMPASS_LLM_SMOKE_MODEL", "local-model"),
            Read("INCIDENTCOMPASS_LLM_SMOKE_API_KEY", "local-smoke-key"),
            ReadPositiveInt("INCIDENTCOMPASS_LLM_SMOKE_RUNS", 3),
            ReadPositiveInt("INCIDENTCOMPASS_LLM_SMOKE_LEASE_SECONDS", 180),
            ReadPositiveInt("INCIDENTCOMPASS_LLM_SMOKE_TIMEOUT_SECONDS", 120),
            Path.GetFullPath(Read("INCIDENTCOMPASS_LLM_SMOKE_RESULT_PATH", Path.Combine(FindRepositoryRoot(), "docs", "phase-5-real-llm-smoke-result.md"))));
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
