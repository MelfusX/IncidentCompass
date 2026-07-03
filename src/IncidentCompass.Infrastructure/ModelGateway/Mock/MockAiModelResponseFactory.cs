using IncidentCompass.Application.Core.ModelClients;
using System.Text.Json;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal static class MockAiModelResponseFactory
{
    public static AiModelResponse CreateResponse(
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

    public static AiToolCall ToolCall(string id, string name, string argumentsJson)
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
