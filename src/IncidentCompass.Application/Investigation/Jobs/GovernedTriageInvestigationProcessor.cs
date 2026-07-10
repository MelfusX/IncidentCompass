using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class GovernedTriageInvestigationProcessor : IClaimedTriageJobProcessor
{
    private const int MaxOrchestratorTurns = 16;

    private readonly ITriageJobInvestigationContextRepository contextRepository;
    private readonly InvestigationModelCaller modelCaller;
    private readonly AnalysisDelegateExecutor delegateExecutor;
    private readonly TriageReportPublisher reportPublisher;
    private readonly TimeProvider timeProvider;

    public GovernedTriageInvestigationProcessor(
        ITriageJobInvestigationContextRepository contextRepository,
        InvestigationModelCaller modelCaller,
        AnalysisDelegateExecutor delegateExecutor,
        TriageReportPublisher reportPublisher,
        TimeProvider timeProvider)
    {
        this.contextRepository = contextRepository;
        this.modelCaller = modelCaller;
        this.delegateExecutor = delegateExecutor;
        this.reportPublisher = reportPublisher;
        this.timeProvider = timeProvider;
    }

    public async Task ProcessAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string workerId,
        CancellationToken cancellationToken)
    {
        var attemptStartedAtUtc = timeProvider.GetUtcNow();
        var context = await contextRepository.GetAsync(job.Id, job.Attempt, cancellationToken);
        var messages = new List<AiChatMessage>
        {
            new(AiMessageRole.System, configuration.Orchestrator.Instructions),
            new(AiMessageRole.User, TriageInvestigationPromptBuilder.BuildOrchestratorPrompt(job, context))
        };

        var reprompts = 0;
        var maxTurns = Math.Max(4, MaxOrchestratorTurns + configuration.Orchestrator.Budget.MaxReprompts);
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var response = await CompleteOrchestratorAsync(job, configuration, attemptStartedAtUtc, messages, cancellationToken);
            var toolCall = response.ProposedToolCalls?.FirstOrDefault();
            if (toolCall is null)
            {
                RepromptOrThrow(configuration, ref reprompts, "Orchestrator did not propose delegate or publish_report after bounded reprompts.");
                messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content));
                messages.Add(new AiChatMessage(
                    AiMessageRole.User,
                    "Validation error: the previous turn did not call delegate or publish_report. Call exactly one available tool."));
                continue;
            }

            messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content, ToolCalls: [toolCall]));
            if (string.Equals(toolCall.Name, "delegate", StringComparison.Ordinal))
            {
                if (await TryDelegateAsync(job, configuration, context, toolCall, attemptStartedAtUtc, messages, reprompts, cancellationToken) is { } nextReprompts)
                {
                    reprompts = nextReprompts;
                    continue;
                }

                continue;
            }

            if (string.Equals(toolCall.Name, "publish_report", StringComparison.Ordinal))
            {
                if (await TryPublishAsync(job, configuration, workerId, toolCall, messages, reprompts, cancellationToken) is { } nextReprompts)
                {
                    reprompts = nextReprompts;
                    continue;
                }

                return;
            }

            RepromptOrThrow(configuration, ref reprompts, "Orchestrator proposed an unknown tool after bounded reprompts: " + toolCall.Name);
            messages.Add(new AiChatMessage(AiMessageRole.Tool, UnknownToolResult(toolCall.Name), toolCall.Id));
            messages.Add(new AiChatMessage(AiMessageRole.User, "Validation error: unknown tool '" + toolCall.Name + "'. Call delegate or publish_report."));
        }

        throw new InvalidOperationException("Orchestrator exceeded the bounded investigation turn limit before publish_report.");
    }

    private async Task<int?> TryPublishAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string workerId,
        AiToolCall toolCall,
        List<AiChatMessage> messages,
        int reprompts,
        CancellationToken cancellationToken)
    {
        try
        {
            await reportPublisher.PublishAsync(job, workerId, toolCall, cancellationToken);
            return null;
        }
        catch (TriageReportValidationException exception)
        {
            RepromptOrThrow(configuration, ref reprompts, "publish_report remained invalid after bounded reprompts: " + exception.Message, exception);
            var validationResult = JsonSerializer.Serialize(new
            {
                errorCode = "publish_report_validation_failed",
                errorMessage = exception.Message
            });
            messages.Add(new AiChatMessage(AiMessageRole.Tool, validationResult, toolCall.Id));
            messages.Add(new AiChatMessage(
                AiMessageRole.User,
                "Validation error: " + exception.Message + " Call publish_report again with the corrected report_json."));
            return reprompts;
        }
    }

    private async Task<int?> TryDelegateAsync(
        TriageJob job,
        TriageConfiguration configuration,
        TriageJobInvestigationContext context,
        AiToolCall toolCall,
        DateTimeOffset attemptStartedAtUtc,
        List<AiChatMessage> messages,
        int reprompts,
        CancellationToken cancellationToken)
    {
        try
        {
            var toolResult = await delegateExecutor.ExecuteAsync(
                job,
                configuration,
                context,
                toolCall,
                attemptStartedAtUtc,
                cancellationToken);
            messages.Add(new AiChatMessage(AiMessageRole.Tool, toolResult, toolCall.Id));
            return null;
        }
        catch (DelegateToolCallValidationException exception)
        {
            RepromptOrThrow(configuration, ref reprompts, "delegate remained invalid after bounded reprompts: " + exception.Message, exception);
            var validationResult = JsonSerializer.Serialize(new
            {
                errorCode = "delegate_validation_failed",
                errorMessage = exception.Message
            });
            messages.Add(new AiChatMessage(AiMessageRole.Tool, validationResult, toolCall.Id));
            messages.Add(new AiChatMessage(AiMessageRole.User, "Validation error: " + exception.Message + " Call delegate again with object arguments containing role and task."));
            return reprompts;
        }
    }

    private async Task<AiModelResponse> CompleteOrchestratorAsync(
        TriageJob job,
        TriageConfiguration configuration,
        DateTimeOffset attemptStartedAtUtc,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var route = configuration.Routes[configuration.Orchestrator.RouteId];
        return await modelCaller.CompleteAsync(
            new TriageJobCallContext(job, configuration, attemptStartedAtUtc, configuration.Orchestrator.RouteId, "orchestrator"),
            route,
            messages,
            OrchestratorToolDefinitions.Create(configuration),
            cancellationToken);
    }

    private static void RepromptOrThrow(
        TriageConfiguration configuration,
        ref int reprompts,
        string message,
        Exception? innerException = null)
    {
        if (reprompts >= configuration.Orchestrator.Budget.MaxReprompts)
        {
            throw new InvalidOperationException(message, innerException);
        }

        reprompts++;
    }

    private static string UnknownToolResult(string toolName)
    {
        return JsonSerializer.Serialize(new { errorCode = "unknown_tool", toolName });
    }
}
