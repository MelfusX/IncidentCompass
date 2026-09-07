using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Observability;
using IncidentCompass.Application.Core.Resilience;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed partial class InvestigationModelCaller(
    IAiModelClient modelClient,
    ITriageLedgerReader ledgerReader,
    TriageLedgerAppender ledgerAppender,
    TimeProvider timeProvider,
    IProviderOutageTracker? providerOutageTracker = null,
    IRuntimeTelemetry? telemetry = null,
    ILogger<InvestigationModelCaller>? logger = null)
{
    private readonly ILogger logger = logger ?? NullLogger<InvestigationModelCaller>.Instance;

    public async Task<AiModelResponse> CompleteAsync(
        TriageJobCallContext context,
        TriageRouteSettings route,
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var usageBefore = await EnsureMayStartAsync(context, route, messages, tools, cancellationToken);
        var request = new AiModelRequest(
            CorrelationId: context.Job.Id.ToString(),
            Model: route.Model,
            Messages: messages,
            Temperature: route.Temperature,
            MaxOutputTokens: route.MaxOutputTokens,
            Tools: tools);

        var startedAtUtc = timeProvider.GetUtcNow();
        using var callCancellation = CreateCallCancellation(context, cancellationToken);
        using var modelTelemetry = telemetry?.StartModelCall();
        try
        {
            callCancellation.Token.ThrowIfCancellationRequested();
            var response = await modelClient.CompleteAsync(request, callCancellation.Token);
            var duration = timeProvider.GetUtcNow() - startedAtUtc;
            telemetry?.RecordModelCall(RuntimeTelemetryOutcome.Succeeded, duration.TotalMilliseconds);
            await RecordModelCallAsync(context, request, response, duration, usageBefore, cancellationToken);
            providerOutageTracker?.RecordProviderSuccess();
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsedMs = (timeProvider.GetUtcNow() - startedAtUtc).TotalMilliseconds;
            telemetry?.RecordModelCall(RuntimeTelemetryOutcome.Cancelled, elapsedMs);
            LogModelCallWallClockCancelled(
                logger, context.Job.Id, context.Role, context.RouteId, context.CallKind, (long)elapsedMs);
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "wall_clock_limit_reached: model call exceeded remaining attempt wall-clock budget.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: CancellationToken.None);
            throw new TriageBudgetExhaustedException(
                TriageBudgetExhaustedException.WallClockReachedDuringCallCode,
                "The triage attempt exceeded MaxWallClockSeconds during a model call.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var elapsedMs = (timeProvider.GetUtcNow() - startedAtUtc).TotalMilliseconds;
            telemetry?.RecordModelCall(RuntimeTelemetryOutcome.Cancelled, elapsedMs);
            LogModelCallHostCancelled(
                logger, context.Job.Id, context.Role, context.RouteId, context.CallKind, (long)elapsedMs);
            throw;
        }
        catch (Exception exception)
        {
            var elapsedMs = (timeProvider.GetUtcNow() - startedAtUtc).TotalMilliseconds;
            telemetry?.RecordModelCall(RuntimeTelemetryOutcome.Failed, elapsedMs);
            LogModelCallFailed(
                logger,
                context.Job.Id,
                context.Role,
                context.RouteId,
                context.CallKind,
                exception.GetType().Name,
                (long)elapsedMs);
            throw;
        }
    }

    private async Task<TriageBudgetLedgerUsage> EnsureMayStartAsync(
        TriageJobCallContext context,
        TriageRouteSettings route,
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var usage = await ledgerReader.ReadBudgetUsageAsync(context.Job, cancellationToken);
        if (usage.TokensSpent >= context.Configuration.Orchestrator.Budget.MaxTokens)
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "max_tokens_reached_before_call: attempt token budget was already reached.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: cancellationToken);
            LogBudgetLimitReached(logger, context.Job.Id, context.Job.Attempt, "max_tokens_reached_before_call");
            throw new TriageBudgetExhaustedException(
                TriageBudgetExhaustedException.MaxTokensReachedCode,
                "The triage attempt token budget was reached before the next model call.");
        }

        var elapsed = timeProvider.GetUtcNow() - context.AttemptStartedAtUtc;
        if (elapsed >= TimeSpan.FromSeconds(context.Configuration.Orchestrator.Budget.MaxWallClockSeconds))
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "wall_clock_limit_reached_before_call: attempt wall-clock budget was already reached.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: cancellationToken);
            LogBudgetLimitReached(logger, context.Job.Id, context.Job.Attempt, "wall_clock_limit_reached_before_call");
            throw new TriageBudgetExhaustedException(
                TriageBudgetExhaustedException.WallClockReachedBeforeCallCode,
                "The triage attempt wall-clock budget was reached before the next model call.");
        }

        var estimatedPromptTokens = TriageTokenEstimator.EstimateMessages(messages, tools);
        if (route.ContextWindowTokens is { } contextWindowTokens && estimatedPromptTokens >= contextWindowTokens)
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "context_window_exceeded: estimated prompt exceeds the route context window.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: cancellationToken);
            LogBudgetLimitReached(logger, context.Job.Id, context.Job.Attempt, "context_window_exceeded");
            throw new TriageBudgetExhaustedException(
                TriageBudgetExhaustedException.ContextWindowExceededCode,
                "The triage prompt exceeds the configured context window.");
        }

        return usage;
    }

    private CancellationTokenSource CreateCallCancellation(
        TriageJobCallContext context,
        CancellationToken cancellationToken)
    {
        var elapsed = timeProvider.GetUtcNow() - context.AttemptStartedAtUtc;
        var remaining = TimeSpan.FromSeconds(context.Configuration.Orchestrator.Budget.MaxWallClockSeconds) - elapsed;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (remaining > TimeSpan.Zero)
        {
            linked.CancelAfter(remaining);
        }
        else
        {
            linked.Cancel();
        }

        return linked;
    }

    private async Task RecordModelCallAsync(
        TriageJobCallContext context,
        AiModelRequest request,
        AiModelResponse response,
        TimeSpan duration,
        TriageBudgetLedgerUsage usageBefore,
        CancellationToken cancellationToken)
    {
        var estimatedInputTokens = TriageTokenEstimator.EstimateMessages(request.Messages, request.Tools);
        var estimatedOutputTokens = TriageTokenEstimator.EstimateText(response.Content);
        var usageSource = response.Usage is { InputTokens: > 0, OutputTokens: > 0, TotalTokens: > 0 } ? "provider" : "estimate";
        var inputTokens = PositiveOrEstimate(response.Usage?.InputTokens, estimatedInputTokens);
        var outputTokens = PositiveOrEstimate(response.Usage?.OutputTokens, estimatedOutputTokens);
        var totalTokens = PositiveOrEstimate(response.Usage?.TotalTokens, inputTokens + outputTokens);

        var metadata = new ModelCallLedgerMetadata(
            context.CallKind,
            context.RouteId,
            response.Model,
            response.Provider,
            usageSource,
            inputTokens,
            outputTokens,
            totalTokens,
            (long)duration.TotalMilliseconds,
            response.ProposedToolCalls?.Count ?? 0);

        await ledgerAppender.AppendModelCallAsync(context.Job, context.Role, metadata, cancellationToken);
        LogModelCallCompleted(
            logger,
            context.Job.Id,
            context.Role,
            metadata.RouteId,
            metadata.Kind,
            metadata.Provider,
            metadata.Model,
            metadata.UsageSource,
            metadata.InputTokens,
            metadata.OutputTokens,
            metadata.TotalTokens,
            metadata.DurationMs,
            metadata.ProposedToolCallCount);

        await ledgerAppender.AppendBudgetEventAsync(
            context.Job,
            "model_call_charged: charged model tokens to the attempt budget.",
            totalTokens,
            workersDelta: null,
            cancellationToken: cancellationToken);
        LogBudgetTokensCharged(logger, context.Job.Id, context.Job.Attempt, totalTokens);

        if (usageBefore.TokensSpent + totalTokens > context.Configuration.Orchestrator.Budget.MaxTokens)
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "max_tokens_overshot_after_call: provider usage exceeded the attempt token budget after completion.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: cancellationToken);
            LogBudgetLimitReached(logger, context.Job.Id, context.Job.Attempt, "max_tokens_overshot_after_call");
        }
    }

    private static int PositiveOrEstimate(int? reportedTokens, int estimatedTokens)
    {
        return reportedTokens is > 0 ? reportedTokens.Value : estimatedTokens;
    }

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Information,
        Message = "Model call for triage job {JobId} role {Role} route {RouteId} kind {CallKind} completed on {Provider}/{Model} with {UsageSource} usage {InputTokens}/{OutputTokens}/{TotalTokens} tokens in {DurationMs}ms proposing {ProposedToolCallCount} tool calls.")]
    private static partial void LogModelCallCompleted(
        ILogger logger,
        Guid jobId,
        string? role,
        string routeId,
        string callKind,
        string provider,
        string model,
        string usageSource,
        int inputTokens,
        int outputTokens,
        int totalTokens,
        long durationMs,
        int proposedToolCallCount);

    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Warning,
        Message = "Model call for triage job {JobId} role {Role} route {RouteId} kind {CallKind} failed with {ExceptionType} after {DurationMs}ms.")]
    private static partial void LogModelCallFailed(
        ILogger logger,
        Guid jobId,
        string? role,
        string routeId,
        string callKind,
        string exceptionType,
        long durationMs);

    [LoggerMessage(
        EventId = 3203,
        Level = LogLevel.Warning,
        Message = "Model call for triage job {JobId} role {Role} route {RouteId} kind {CallKind} was cancelled after {DurationMs}ms because the attempt wall-clock budget ran out.")]
    private static partial void LogModelCallWallClockCancelled(
        ILogger logger,
        Guid jobId,
        string? role,
        string routeId,
        string callKind,
        long durationMs);

    [LoggerMessage(
        EventId = 3204,
        Level = LogLevel.Information,
        Message = "Model call for triage job {JobId} role {Role} route {RouteId} kind {CallKind} was cancelled after {DurationMs}ms by host shutdown.")]
    private static partial void LogModelCallHostCancelled(
        ILogger logger,
        Guid jobId,
        string? role,
        string routeId,
        string callKind,
        long durationMs);

    [LoggerMessage(
        EventId = 3211,
        Level = LogLevel.Debug,
        Message = "Triage job {JobId} attempt {Attempt} charged {TokensDelta} model tokens to the attempt budget.")]
    private static partial void LogBudgetTokensCharged(
        ILogger logger,
        Guid jobId,
        int attempt,
        int tokensDelta);

    [LoggerMessage(
        EventId = 3212,
        Level = LogLevel.Warning,
        Message = "Triage job {JobId} attempt {Attempt} hit budget limit {BudgetReason}.")]
    private static partial void LogBudgetLimitReached(
        ILogger logger,
        Guid jobId,
        int attempt,
        string budgetReason);
}
