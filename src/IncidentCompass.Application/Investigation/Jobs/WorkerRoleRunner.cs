using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerRoleRunner(
    InvestigationModelCaller modelCaller,
    WorkerToolCallExecutor toolCallExecutor)
{
    private const int WorkerTurnSlack = 4;
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
        var maxTurns = Math.Max(4, configuration.Orchestrator.Budget.MaxReprompts + role.Tools.Count + WorkerTurnSlack);
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var response = await modelCaller.CompleteAsync(
                new TriageJobCallContext(job, configuration, attemptStartedAtUtc, role.RouteId, "worker", roleName),
                route,
                messages,
                toolSurface.Count > 0 ? toolSurface : null,
                cancellationToken);
            var toolCall = response.ProposedToolCalls is { Count: > 0 } proposedToolCalls
                ? proposedToolCalls[0]
                : null;
            if (toolCall is not null)
            {
                messages.Add(new AiChatMessage(AiMessageRole.Assistant, response.Content, ToolCalls: [toolCall]));
                var toolResult = await toolCallExecutor.ExecuteAsync(job, configuration, context, roleName, toolCall, cancellationToken);
                messages.Add(new AiChatMessage(AiMessageRole.Tool, toolResult, toolCall.Id));
                continue;
            }

            try
            {
                AnalysisWorkerOutputSchemaValidator.Validate(response.Content, role.OutputSchema, roleName);
                return response.Content;
            }
            catch (Exception exception) when (IsRepromptableWorkerOutput(exception))
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
                    "Validation error: " + exception.Message + " Return only JSON matching the configured schema."));
            }
        }

        throw new TriageBudgetExhaustedException(
            TriageBudgetExhaustedException.WorkerTurnLimitReachedCode,
            "Worker exceeded the bounded tool/reprompt turn limit.");
    }

    /// <summary>
    /// Only the worker's own output failing schema validation may be reprompted: the model can correct
    /// its JSON on the next bounded turn. <see cref="AnalysisWorkerOutputSchemaValidator"/> reports
    /// those as <see cref="InvalidOperationException"/> (schema shape) or <see cref="JsonException"/>
    /// (unparsable output or schema). Budget exhaustion and governance denial are deliberate
    /// fail-closed stops that must leave this loop and dead-letter the attempt, so they are excluded
    /// by classification rather than by where a throw happens to sit relative to the try block.
    /// </summary>
    private static bool IsRepromptableWorkerOutput(Exception exception)
    {
        return exception is InvalidOperationException or JsonException &&
            TriageNonRetryableFailureClassifier.TryGetErrorCode(exception) is null;
    }
}
