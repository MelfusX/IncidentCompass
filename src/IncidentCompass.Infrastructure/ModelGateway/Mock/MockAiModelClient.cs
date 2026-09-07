using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal sealed class MockAiModelClient : IAiModelClient
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

        if (MockIncidentCompassScripts.IsMemoryWorkerRequest(request))
        {
            return Task.FromResult(MockIncidentCompassScripts.MemoryWorkerResponse(request, hasToolResult));
        }

        if (MockIncidentCompassScripts.IsAnalysisWorkerRequest(request))
        {
            return Task.FromResult(MockAiModelResponseFactory.CreateResponse(
                request,
                MockIncidentCompassScripts.AnalysisWorkerJson(lastUserMessage),
                []));
        }

        if (MockIncidentCompassScripts.IsIncidentCompassOrchestratorRequest(request))
        {
            return Task.FromResult(MockIncidentCompassScripts.OrchestratorResponse(request));
        }

        var canProposeToolCalls = request.Tools is { Count: > 0 };
        var proposedToolCalls = hasToolResult || !canProposeToolCalls
            ? []
            : MockGenericToolIntent.ProposeToolCalls(lastUserMessage);

        var content = string.IsNullOrWhiteSpace(lastUserMessage)
            ? "Mock model response."
            : hasToolResult
                ? "Mock agent response using backend tool result."
                : proposedToolCalls.Count > 0
                    ? "Mock model proposed a backend tool call."
                    : $"Mock model response: {lastUserMessage}";

        return Task.FromResult(MockAiModelResponseFactory.CreateResponse(request, content, proposedToolCalls));
    }
}
