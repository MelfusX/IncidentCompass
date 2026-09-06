using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class WorkerRoleRunner(
    InvestigationModelCaller modelCaller,
    WorkerToolCallExecutor toolCallExecutor)
{
    /// <summary>
    /// Turns the worker's bound allows beyond one per granted tool and one per configured reprompt.
    /// The role's own turn budget is deliberately not the orchestrator's <c>Budget.MaxTurns</c>: that
    /// one bounds orchestrator work turns for the whole attempt, while this bounds a single worker.
    /// </summary>
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
        if (!configuration.Routes.TryGetValue(role.RouteId, out var route))
        {
            // Load-time validation rejects a configuration like this, but the worker loop must not
            // depend on having been handed a validated configuration: a rehydrated snapshot whose role
            // names a route it does not contain fails closed under its own governance error code
            // instead of throwing KeyNotFoundException out of the claim loop.
            throw new TriageGovernanceDeniedException(
                TriageGovernanceDeniedException.WorkerRouteMissingCode,
                "Role '" + roleName + "' names route '" + role.RouteId +
                "', which is not a configured route in this triage configuration.");
        }

        var toolSurface = toolCallExecutor.CreateToolSurface(configuration, role);
        var messages = new List<AiChatMessage>
        {
            new(AiMessageRole.System, role.Instructions),
            new(AiMessageRole.User, TriageInvestigationPromptBuilder.BuildWorkerPrompt(roleName, task, job, context))
        };

        var reprompts = 0;
        var maxTurns = configuration.Orchestrator.Budget.MaxReprompts + role.Tools.Count + WorkerTurnSlack;
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var response = await modelCaller.CompleteAsync(
                new TriageJobCallContext(job, configuration, attemptStartedAtUtc, role.RouteId, TriageModelCallKinds.Worker, roleName),
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
