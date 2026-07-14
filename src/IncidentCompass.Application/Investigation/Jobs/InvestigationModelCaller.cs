using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Resilience;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class InvestigationModelCaller(
    IAiModelClient modelClient,
    ITriageLedgerReader ledgerReader,
    TriageLedgerAppender ledgerAppender,
    TimeProvider timeProvider,
    IProviderOutageTracker? providerOutageTracker = null)
{
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
        try
        {
            callCancellation.Token.ThrowIfCancellationRequested();
            var response = await modelClient.CompleteAsync(request, callCancellation.Token);
            var duration = timeProvider.GetUtcNow() - startedAtUtc;
            await RecordModelCallAsync(context, request, response, duration, usageBefore, cancellationToken);
            providerOutageTracker?.RecordProviderSuccess();
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "wall_clock_limit_reached: model call exceeded remaining attempt wall-clock budget.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: CancellationToken.None);
            throw new InvalidOperationException("The triage attempt exceeded MaxWallClockSeconds during a model call.");
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
            throw new InvalidOperationException("The triage attempt token budget was reached before the next model call.");
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
            throw new InvalidOperationException("The triage attempt wall-clock budget was reached before the next model call.");
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
            throw new InvalidOperationException("The triage prompt exceeds the configured context window.");
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

        await ledgerAppender.AppendModelCallAsync(
            context.Job,
            context.Role,
            new
            {
                kind = context.CallKind,
                routeId = context.RouteId,
                model = response.Model,
                provider = response.Provider,
                usageSource,
                inputTokens,
                outputTokens,
                totalTokens,
                durationMs = (long)duration.TotalMilliseconds,
                proposedToolCallCount = response.ProposedToolCalls?.Count ?? 0
            },
            cancellationToken);

        await ledgerAppender.AppendBudgetEventAsync(
            context.Job,
            "model_call_charged: charged model tokens to the attempt budget.",
            totalTokens,
            workersDelta: null,
            cancellationToken: cancellationToken);

        if (usageBefore.TokensSpent + totalTokens > context.Configuration.Orchestrator.Budget.MaxTokens)
        {
            await ledgerAppender.AppendBudgetEventAsync(
                context.Job,
                "max_tokens_overshot_after_call: provider usage exceeded the attempt token budget after completion.",
                tokensDelta: null,
                workersDelta: null,
                cancellationToken: cancellationToken);
        }
    }

    private static int PositiveOrEstimate(int? reportedTokens, int estimatedTokens)
    {
        return reportedTokens is > 0 ? reportedTokens.Value : estimatedTokens;
    }
}
