using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.UnitTests;

public sealed class InvestigationModelCallerTests
{
    [Fact]
    public async Task CompleteAsync_AllZeroProviderUsageChargesEstimatedTokens()
    {
        var writer = new RecordingLedgerWriter();
        var model = new StaticModelClient(new AiModelUsage(0, 0, 0));
        var caller = CreateCaller(model, writer, TimeProvider.System);
        var context = CreateContext(TimeProvider.System.GetUtcNow(), maxWallClockSeconds: 60);

        await caller.CompleteAsync(
            context,
            context.Configuration.Routes[context.RouteId],
            [new AiChatMessage(AiMessageRole.User, "Summarize the incident.")],
            tools: null,
            CancellationToken.None);

        var modelCall = Assert.Single(writer.Requests, request => request.EventType == TriageLedgerEventType.ModelCall);
        using var metadata = JsonDocument.Parse(modelCall.Rationale!);
        Assert.Equal("estimate", metadata.RootElement.GetProperty("usageSource").GetString());
        Assert.True(metadata.RootElement.GetProperty("totalTokens").GetInt32() > 0);

        var charge = Assert.Single(writer.Requests, request => request.EventType == TriageLedgerEventType.BudgetEvent);
        Assert.True(charge.TokensDelta > 0);
    }

    [Fact]
    public async Task CompleteAsync_WallClockReachedBeforeCallDoesNotInvokeModel()
    {
        var now = DateTimeOffset.UtcNow;
        var writer = new RecordingLedgerWriter();
        var model = new StaticModelClient(new AiModelUsage(1, 1, 2));
        var caller = CreateCaller(model, writer, new ConstantTimeProvider(now));
        var context = CreateContext(now.AddSeconds(-2), maxWallClockSeconds: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => caller.CompleteAsync(
            context,
            context.Configuration.Routes[context.RouteId],
            [new AiChatMessage(AiMessageRole.User, "This call should not start.")],
            tools: null,
            CancellationToken.None));

        Assert.Contains("wall-clock budget", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, model.CallCount);
        Assert.Contains(writer.Requests, request =>
            request.EventType == TriageLedgerEventType.BudgetEvent &&
            request.Rationale!.Contains("wall_clock_limit_reached_before_call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WallClockExpiresBetweenGuardAndCallDoesNotInvokeModel()
    {
        var now = DateTimeOffset.UtcNow;
        var writer = new RecordingLedgerWriter();
        var model = new StaticModelClient(new AiModelUsage(1, 1, 2));
        var caller = CreateCaller(
            model,
            writer,
            new SequenceTimeProvider(now.AddMilliseconds(900), now.AddSeconds(1), now.AddSeconds(1)));
        var context = CreateContext(now, maxWallClockSeconds: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => caller.CompleteAsync(
            context,
            context.Configuration.Routes[context.RouteId],
            [new AiChatMessage(AiMessageRole.User, "The boundary expires before dispatch.")],
            tools: null,
            CancellationToken.None));

        Assert.Contains("MaxWallClockSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, model.CallCount);
        Assert.Contains(writer.Requests, request =>
            request.EventType == TriageLedgerEventType.BudgetEvent &&
            request.Rationale!.Contains("wall_clock_limit_reached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WallClockCancelAfterStopsMidCall()
    {
        var writer = new RecordingLedgerWriter();
        var model = new WaitingModelClient();
        var caller = CreateCaller(model, writer, TimeProvider.System);
        var context = CreateContext(TimeProvider.System.GetUtcNow(), maxWallClockSeconds: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => caller.CompleteAsync(
            context,
            context.Configuration.Routes[context.RouteId],
            [new AiChatMessage(AiMessageRole.User, "Wait until cancellation.")],
            tools: null,
            CancellationToken.None));

        Assert.Contains("MaxWallClockSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, model.CallCount);
        Assert.Contains(writer.Requests, request =>
            request.EventType == TriageLedgerEventType.BudgetEvent &&
            request.Rationale!.Contains("wall_clock_limit_reached", StringComparison.Ordinal));
    }

    private static InvestigationModelCaller CreateCaller(
        IAiModelClient modelClient,
        RecordingLedgerWriter writer,
        TimeProvider timeProvider)
    {
        return new InvestigationModelCaller(
            modelClient,
            new StaticLedgerReader(),
            new TriageLedgerAppender(writer),
            timeProvider);
    }

    private static TriageJobCallContext CreateContext(DateTimeOffset attemptStartedAtUtc, int maxWallClockSeconds)
    {
        var configuration = CreateConfiguration(maxWallClockSeconds);
        return new TriageJobCallContext(
            CreateJob(attemptStartedAtUtc),
            configuration,
            attemptStartedAtUtc,
            "report-chat",
            "orchestrator");
    }

    private static TriageConfiguration CreateConfiguration(int maxWallClockSeconds)
    {
        return new TriageConfiguration(
            "config-hash",
            new Dictionary<string, TriageProviderSettings>
            {
                ["mock"] = new("Mock", Endpoint: null, ApiKeySecretRef: null)
            },
            new Dictionary<string, TriageRouteSettings>
            {
                ["report-chat"] = new("Chat", "mock", "test-model", Temperature: 0, MaxOutputTokens: 100, ContextWindowTokens: 8192)
            },
            new OrchestratorSettings(
                "Investigate and publish a report.",
                "report-chat",
                ["delegate", "publish_report"],
                new OrchestratorBudgetSettings(MaxWorkers: 2, MaxTokens: 100000, MaxWallClockSeconds: maxWallClockSeconds, MaxReprompts: 1)),
            new Dictionary<string, TriageRoleSettings>(),
            new Dictionary<string, TriageToolSettings>(),
            [],
            new IngestionSettings("local", ["tester"]),
            new FaultGroupingSettings(15, 30, 1, new MassIssueSettings(5, "strong")));
    }

    private static TriageJob CreateJob(DateTimeOffset now)
    {
        return new TriageJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TriageJobStatus.Processing,
            Attempt: 1,
            LockedBy: "worker-test",
            LockedUntilUtc: now.AddMinutes(5),
            NextAttemptAtUtc: null,
            LastErrorCode: null,
            LastErrorMessage: null,
            ConfigHash: "config-hash",
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
    }

    private sealed class StaticModelClient(AiModelUsage usage) : IAiModelClient
    {
        public int CallCount { get; private set; }

        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AiModelResponse(
                "A concise response.",
                request.Model,
                "test-provider",
                usage,
                request.CorrelationId,
                ProposedToolCalls: []));
        }
    }

    private sealed class WaitingModelClient : IAiModelClient
    {
        public int CallCount { get; private set; }

        public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The waiting model should be cancelled before returning.");
        }
    }

    private sealed class StaticLedgerReader : ITriageLedgerReader
    {
        public Task<TriageBudgetLedgerUsage> ReadBudgetUsageAsync(TriageJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new TriageBudgetLedgerUsage(0, 0));

        public Task<int> CountPolicyDecisionsAsync(
            TriageJob job,
            string toolName,
            string scope,
            TriageLedgerDecision decision,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<bool> HasSuccessfulToolResultAsync(
            TriageJob job,
            string toolName,
            string scope,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<TriageLedgerEntry>> ReadByFaultIdAsync(Guid faultId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TriageLedgerEntry>>([]);
    }

    private sealed class RecordingLedgerWriter : ITriageLedgerWriter
    {
        private long nextId;

        public List<TriageLedgerAppendRequest> Requests { get; } = [];

        public Task<TriageLedgerEntry> AppendAsync(TriageLedgerAppendRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var entry = new TriageLedgerEntry(
                ++nextId,
                request.FaultId,
                request.JobId,
                request.Attempt,
                request.EventType,
                request.Role,
                request.ToolName,
                request.Rationale,
                request.Decision,
                request.DecisionReason,
                request.PayloadRef,
                request.ConfigHash,
                DateTimeOffset.UtcNow,
                request.ToolStatus,
                request.TokensDelta,
                request.WorkersDelta);
            return Task.FromResult(entry);
        }
    }

    private sealed class ConstantTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] timestamps) : TimeProvider
    {
        private int index;

        public override DateTimeOffset GetUtcNow()
        {
            if (index >= timestamps.Length)
            {
                return timestamps[^1];
            }

            return timestamps[index++];
        }
    }
}