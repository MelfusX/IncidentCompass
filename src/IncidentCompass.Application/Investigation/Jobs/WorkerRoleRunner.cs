using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerRoleRunner(
    InvestigationModelCaller modelCaller,
    WorkerToolCallExecutor toolCallExecutor)
{
    public async Task<string> RunAsync(
        TriageJob job,
        TriageConfiguration configuration,
        TriageJobInvestigationContext context,
        string roleName,
        TriageRoleSettings role,
        string task,
        DateTimeOffset attemptStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var route = configuration.Routes[role.RouteId];
        var toolSurface = toolCallExecutor.CreateToolSurface(configuration, role);
        var messages = new List<AiChatMessage>
        {
            new(AiMessageRole.System, role.Instructions),
            new(AiMessageRole.User, TriageInvestigationPromptBuilder.BuildWorkerPrompt(roleName, task, job, context))
        };

        var reprompts = 0;
        var maxTurns = Math.Max(4, configuration.Orchestrator.Budget.MaxWorkers + configuration.Orchestrator.Budget.MaxReprompts + role.Tools.Count + 4);
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var response = await modelCaller.CompleteAsync(
                new TriageJobCallContext(job, configuration, attemptStartedAtUtc, role.RouteId, "worker", roleName),
                route,
                messages,
                toolSurface.Count > 0 ? toolSurface : null,
                cancellationToken);
            var toolCall = response.ProposedToolCalls?.FirstOrDefault();
            if (toolCall is not null)
            {
                messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content, ToolCalls: [toolCall]));
                var toolResult = await toolCallExecutor.ExecuteAsync(job, configuration, roleName, toolCall, cancellationToken);
                messages.Add(new AiChatMessage(AiMessageRole.Tool, toolResult, toolCall.Id));
                continue;
            }

            try
            {
                AnalysisWorkerOutputSchemaValidator.Validate(response.Content, role.OutputSchema);
                return response.Content;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException)
            {
                if (reprompts >= configuration.Orchestrator.Budget.MaxReprompts)
                {
                    throw new InvalidOperationException(
                        "Worker output remained invalid after bounded reprompts: " + exception.Message,
                        exception);
                }

                reprompts++;
                messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content));
                messages.Add(new AiChatMessage(
                    AiMessageRole.User,
                    "Validation error: " + exception.Message + " Return only JSON matching the configured schema. keyFacts must be an array of plain strings."));
            }
        }

        throw new InvalidOperationException("Worker exceeded the bounded tool/reprompt turn limit.");
    }
}