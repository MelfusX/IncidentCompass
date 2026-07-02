using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class GovernedTriageInvestigationProcessor : IClaimedTriageJobProcessor
{
    private readonly IAiModelClient modelClient;
    private readonly ITriageJobInvestigationContextRepository contextRepository;
    private readonly AnalysisDelegateExecutor delegateExecutor;
    private readonly MinimalTriageReportPublisher reportPublisher;

    public GovernedTriageInvestigationProcessor(
        IAiModelClient modelClient,
        ITriageJobInvestigationContextRepository contextRepository,
        ITriageArtifactRepository artifactRepository,
        ITriageLedgerWriter ledgerWriter,
        IMinimalTriageReportRepository reportRepository,
        TimeProvider timeProvider)
    {
        var ledgerAppender = new TriageLedgerAppender(ledgerWriter);
        this.modelClient = modelClient;
        this.contextRepository = contextRepository;
        delegateExecutor = new AnalysisDelegateExecutor(modelClient, artifactRepository, ledgerAppender, timeProvider);
        reportPublisher = new MinimalTriageReportPublisher(reportRepository, ledgerAppender);
    }

    public async Task ProcessAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string workerId,
        CancellationToken cancellationToken)
    {
        var context = await contextRepository.GetAsync(job.Id, cancellationToken);
        var messages = new List<AiChatMessage>
        {
            new(AiMessageRole.System, configuration.Orchestrator.Instructions),
            new(AiMessageRole.User, TriageInvestigationPromptBuilder.BuildOrchestratorPrompt(job, context))
        };

        for (var turn = 0; turn < configuration.Orchestrator.Budget.MaxWorkers + 2; turn++)
        {
            var response = await CompleteOrchestratorAsync(job, configuration, messages, cancellationToken);
            var toolCall = response.ProposedToolCalls?.FirstOrDefault()
                ?? throw new InvalidOperationException("Orchestrator did not propose delegate or publish_report.");

            messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content, ToolCalls: [toolCall]));
            if (string.Equals(toolCall.Name, "delegate", StringComparison.Ordinal))
            {
                var toolResult = await delegateExecutor.ExecuteAsync(job, configuration, context, toolCall, cancellationToken);
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

        throw new InvalidOperationException("Orchestrator exceeded the Phase 2 turn limit before publish_report.");
    }

    private async Task<AiModelResponse> CompleteOrchestratorAsync(
        TriageJob job,
        TriageConfiguration configuration,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var route = configuration.Routes[configuration.Orchestrator.RouteId];
        return await modelClient.CompleteAsync(
            new AiModelRequest(
                CorrelationId: job.Id.ToString(),
                Model: route.Model,
                Messages: messages,
                Temperature: route.Temperature,
                MaxOutputTokens: route.MaxOutputTokens,
                Tools: OrchestratorToolDefinitions.Create(configuration)),
            cancellationToken);
    }

    private static string UnknownToolResult(string toolName)
    {
        return JsonSerializer.Serialize(new { errorCode = "unknown_tool", toolName });
    }
}
