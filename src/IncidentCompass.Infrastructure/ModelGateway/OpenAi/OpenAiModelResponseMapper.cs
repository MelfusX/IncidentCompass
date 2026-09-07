using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal static class OpenAiModelResponseMapper
{
    public static AiModelResponse Map(
        string responseContent,
        AiModelRequest request)
    {
        var completion = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(
            responseContent,
            OpenAiCompatibleJson.Options);
        var choices = completion?.Choices;
        var message = choices is { Count: > 0 } && choices[0] is { } choice ? choice.Message : null;
        var content = message?.Content;
        var proposedToolCalls = message?.ToolCalls?
            .Select(ToAiToolCall)
            .Where(static toolCall => toolCall is not null)
            .Select(static toolCall => toolCall!)
            .ToArray() ?? [];

        if (string.IsNullOrWhiteSpace(content) && proposedToolCalls.Length == 0)
        {
            throw OpenAiModelErrorMapper.EmptyResponse();
        }

        return new AiModelResponse(
            Content: content ?? string.Empty,
            Model: completion?.Model ?? request.Model,
            Provider: OpenAiModelProvider.Name,
            Usage: completion?.Usage is null
                ? null
                : new AiModelUsage(
                    completion.Usage.PromptTokens,
                    completion.Usage.CompletionTokens,
                    completion.Usage.TotalTokens),
            CorrelationId: request.CorrelationId,
            ProposedToolCalls: proposedToolCalls);
    }

    private static AiToolCall? ToAiToolCall(OpenAiToolCall toolCall)
    {
        if (toolCall.Function is null ||
            string.IsNullOrWhiteSpace(toolCall.Function.Name))
        {
            return null;
        }

        return new AiToolCall(
            string.IsNullOrWhiteSpace(toolCall.Id) ? Guid.NewGuid().ToString("n") : toolCall.Id,
            toolCall.Function.Name,
            "v1",
            ReadArguments(toolCall.Function.Arguments));
    }

    private static JsonElement ReadArguments(string? argumentsJson)
    {
        try
        {
            using var argumentsDocument = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return argumentsDocument.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(argumentsJson);
        }
    }
}
