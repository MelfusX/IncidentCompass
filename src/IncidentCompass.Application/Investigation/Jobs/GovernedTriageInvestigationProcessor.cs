using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class GovernedTriageInvestigationProcessor : IClaimedTriageJobProcessor
{
    private readonly ITriageJobInvestigationContextRepository contextRepository;
    private readonly InvestigationModelCaller modelCaller;
    private readonly AnalysisDelegateExecutor delegateExecutor;
    private readonly MinimalTriageReportPublisher reportPublisher;
    private readonly TimeProvider timeProvider;

    public GovernedTriageInvestigationProcessor(
        ITriageJobInvestigationContextRepository contextRepository,
        InvestigationModelCaller modelCaller,
        AnalysisDelegateExecutor delegateExecutor,
        MinimalTriageReportPublisher reportPublisher,
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
        var context = await contextRepository.GetAsync(job.Id, cancellationToken);
        var messages = new List<AiChatMessage>
        {
            new(AiMessageRole.System, configuration.Orchestrator.Instructions),
            new(AiMessageRole.User, TriageInvestigationPromptBuilder.BuildOrchestratorPrompt(job, context))
        };

        var reprompts = 0;
        var maxTurns = Math.Max(4, configuration.Orchestrator.Budget.MaxWorkers + configuration.Orchestrator.Budget.MaxReprompts + 4);
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var response = await CompleteOrchestratorAsync(job, configuration, attemptStartedAtUtc, messages, cancellationToken);
            var toolCall = response.ProposedToolCalls?.FirstOrDefault();
            if (toolCall is null)
            {
                if (reprompts >= configuration.Orchestrator.Budget.MaxReprompts)
                {
                    throw new InvalidOperationException("Orchestrator did not propose delegate or publish_report after bounded reprompts.");
                }

                reprompts++;
                messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content));
                messages.Add(new AiChatMessage(
                    AiMessageRole.User,
                    "Validation error: the previous turn did not call delegate or publish_report. Call exactly one available tool."));
                continue;
            }

            messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content, ToolCalls: [toolCall]));
            if (string.Equals(toolCall.Name, "delegate", StringComparison.Ordinal))
            {
                var toolResult = await delegateExecutor.ExecuteAsync(
                    job,
                    configuration,
                    context,
                    toolCall,
                    attemptStartedAtUtc,
                    cancellationToken);
                messages.Add(new AiChatMessage(AiMessageRole.Tool, toolResult, toolCall.Id));
                continue;
            }

            if (string.Equals(toolCall.Name, "publish_report", StringComparison.Ordinal))
            {
                await reportPublisher.PublishAsync(job, workerId, toolCall, cancellationToken);
                return;
            }

            messages.Add(new AiChatMessage(AiMessageRole.Tool, UnknownToolResult(toolCall.Name), toolCall.Id));
        }

        throw new InvalidOperationException("Orchestrator exceeded the bounded investigation turn limit before publish_report.");
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

    private static string UnknownToolResult(string toolName)
    {
        return JsonSerializer.Serialize(new { errorCode = "unknown_tool", toolName });
    }
}