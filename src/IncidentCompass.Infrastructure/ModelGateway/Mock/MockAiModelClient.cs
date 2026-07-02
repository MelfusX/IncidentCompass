using IncidentCompass.Application.Core.ModelClients;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal sealed partial class MockAiModelClient : IAiModelClient
{
    public Task<AiModelResponse> CompleteAsync(
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lastUserMessage = request.Messages
            .LastOrDefault(static message => message.Role == AiMessageRole.User)
            ?.Content ?? string.Empty;
        var hasToolResult = request.Messages.Any(static message =>
            message.Role == AiMessageRole.Tool);

        if (IsAnalysisWorkerRequest(request))
        {
            return Task.FromResult(CreateResponse(request, AnalysisWorkerJson(lastUserMessage), []));
        }

        if (IsIncidentCompassOrchestratorRequest(request))
        {
            var scriptedToolCall = hasToolResult
                ? ToolCall("incidentcompass-publish-report-1", "publish_report", """
                    {"report_json":{"status":"Completed","summary":"Mock analysis completed for the incident.","classification":"SimpleKnownError","confidence":"Medium","limitations":[],"recommendedNextAction":"Review the affected service logs and confirm the timeout path."}}
                    """)
                : ToolCall("incidentcompass-delegate-analysis-1", "delegate", """
                    {"role":"analysis","task":"Extract key facts and propose a candidate classification from the grounded intake facts."}
                    """);
            return Task.FromResult(CreateResponse(request, "Mock IncidentCompass orchestrator step.", [scriptedToolCall]));
        }

        var canProposeToolCalls = request.Tools is { Count: > 0 };
        var proposedToolCalls = hasToolResult || !canProposeToolCalls
            ? []
            : ProposeToolCalls(lastUserMessage);

        var content = string.IsNullOrWhiteSpace(lastUserMessage)
            ? "Mock model response."
            : hasToolResult
                ? "Mock agent response using backend tool result."
                : proposedToolCalls.Count > 0
                    ? "Mock model proposed a backend tool call."
                    : $"Mock model response: {lastUserMessage}";

        return Task.FromResult(CreateResponse(request, content, proposedToolCalls));
    }

    private static AiModelResponse CreateResponse(
        AiModelRequest request,
        string content,
        IReadOnlyList<AiToolCall> proposedToolCalls)
    {
        var inputTokens = request.Messages.Sum(static message => CountApproximateTokens(message.Content));
        var outputTokens = CountApproximateTokens(content);

        return new AiModelResponse(
            Content: content,
            Model: request.Model,
            Provider: "mock",
            Usage: new AiModelUsage(inputTokens, outputTokens, inputTokens + outputTokens),
            CorrelationId: request.CorrelationId,
            ProposedToolCalls: proposedToolCalls);
    }

    private static bool IsIncidentCompassOrchestratorRequest(AiModelRequest request)
    {
        var toolNames = request.Tools?.Select(static tool => tool.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        return toolNames.SetEquals(["delegate", "publish_report"]);
    }

    private static bool IsAnalysisWorkerRequest(AiModelRequest request)
    {
        if (request.Tools is { Count: > 0 })
        {
            return false;
        }

        return request.Messages.Any(static message =>
            message.Role == AiMessageRole.System &&
            message.Content.Contains("analysis", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("worker", StringComparison.OrdinalIgnoreCase));
    }

    private static string AnalysisWorkerJson(string message)
    {
        var summary = message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            ? "The trigger signal reports a timeout on the affected service."
            : "The trigger signal contains enough grounded intake facts for a first classification.";

        return JsonSerializer.Serialize(new
        {
            keyFacts = new[] { summary, "The analysis worker used only grounded intake context." },
            candidateClassification = "SimpleKnownError",
            needsDeeperContext = false,
            rationale = "The mock analysis found a bounded known-error style failure from the trigger signal."
        });
    }

    private static IReadOnlyList<AiToolCall> ProposeToolCalls(string message)
    {
        // Regex intent detection is for deterministic local demos/tests, not production tool selection.
        if (HasProfileIntent(message))
        {
            return [ToolCall("mock-tool-1", "GetCurrentUserProfile", "{}")];
        }

        if (HasSupportTicketIntent(message))
        {
            return
            [
                ToolCall("mock-tool-1", "CreateSupportTicket",
                    """{"title":"Demo support ticket","description":"Created by the mock agent.","priority":"normal"}""")
            ];
        }

        if (HasDraftEmailIntent(message))
        {
            return
            [
                ToolCall("mock-tool-1", "DraftEmail",
                    """{"to":"demo@example.test","subject":"Demo draft","body":"This is a draft created by the mock agent."}""")
            ];
        }

        if (HasDeleteDocumentIntent(message))
        {
            return [ToolCall("mock-tool-1", "DeleteDocument", """{"documentId":"00000000-0000-0000-0000-000000000000"}""")];
        }

        return [];
    }

    private static bool HasProfileIntent(string message)
    {
        return ProfileIntentRegex().IsMatch(message)
            || ProfileShorthandRegex().IsMatch(message);
    }

    private static bool HasSupportTicketIntent(string message) =>
        SupportTicketIntentRegex().IsMatch(message);

    private static bool HasDraftEmailIntent(string message) =>
        DraftEmailIntentRegex().IsMatch(message);

    private static bool HasDeleteDocumentIntent(string message) =>
        DeleteDocumentIntentRegex().IsMatch(message);

    [GeneratedRegex(
        @"\b(use|show|get|fetch|read|check|lookup|look\s+up)\b.*\b(my|current|user)\b.*\b(profile|account|details?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIntentRegex();

    [GeneratedRegex(
        @"\b(my|current)\s+profile\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileShorthandRegex();

    [GeneratedRegex(
        @"\b(create|open|submit|file|raise|log)\b.*\b(support\s+)?ticket\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SupportTicketIntentRegex();

    [GeneratedRegex(
        @"\bdraft\s+(an?\s+)?email\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DraftEmailIntentRegex();

    [GeneratedRegex(
        @"\b(delete|remove)\b.*\b(document|file)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeleteDocumentIntentRegex();

    private static AiToolCall ToolCall(string id, string name, string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        return new AiToolCall(id, name, "v1", arguments.RootElement.Clone());
    }

    private static int CountApproximateTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }
}
