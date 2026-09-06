using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.ModelGateway.OpenAi.Dtos;
using IncidentCompass.Infrastructure.OpenAiCompatible;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal static class OpenAiModelRequestFactory
{
    public static string CreatePayloadJson(AiModelRequest request)
    {
        var payload = new OpenAiChatCompletionRequest(
            Model: request.Model,
            Messages: request.Messages.Select(ToOpenAiMessage).ToArray(),
            Temperature: request.Temperature,
            MaxTokens: request.MaxOutputTokens,
            Tools: request.Tools is { Count: > 0 }
                ? request.Tools.Select(ToOpenAiTool).ToArray()
                : null);

        return JsonSerializer.Serialize(payload, OpenAiCompatibleJson.Options);
    }

    public static HttpRequestMessage CreateHttpRequest(
        OpenAiCompatibleModelClientOptions clientOptions,
        AiModelRequest request,
        string payloadJson,
        Uri endpointUri,
        string idempotencyKey)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientOptions.ApiKey);
        httpRequest.Headers.Add("X-Correlation-Id", request.CorrelationId);
        httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        if (!string.IsNullOrWhiteSpace(clientOptions.Organization))
        {
            httpRequest.Headers.Add("OpenAI-Organization", clientOptions.Organization);
        }

        httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static OpenAiChatMessage ToOpenAiMessage(AiChatMessage message)
    {
        return new OpenAiChatMessage(
            Role: ToOpenAiRole(message.Role),
            Content: message.Content,
            ToolCallId: message.Role == AiMessageRole.Tool ? message.ToolCallId : null,
            ToolCalls: message.Role == AiMessageRole.Assistant && message.ToolCalls is { Count: > 0 }
                ? message.ToolCalls.Select(ToOpenAiToolCall).ToArray()
                : null);
    }

    private static string ToOpenAiRole(AiMessageRole role)
    {
        return role switch
        {
            AiMessageRole.System => "system",
            AiMessageRole.User => "user",
            AiMessageRole.Assistant => "assistant",
            AiMessageRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }

    private static OpenAiToolCall ToOpenAiToolCall(AiToolCall toolCall)
    {
        return new OpenAiToolCall(
            toolCall.Id,
            "function",
            new OpenAiToolCallFunction(
                toolCall.Name,
                SerializeToolArguments(toolCall.Arguments)));
    }

    private static string SerializeToolArguments(JsonElement arguments)
    {
        return arguments.ValueKind switch
        {
            JsonValueKind.String => arguments.GetString() ?? string.Empty,
            JsonValueKind.Undefined => "{}",
            _ => arguments.GetRawText()
        };
    }

    private static OpenAiTool ToOpenAiTool(AiToolDefinition tool)
    {
        return new OpenAiTool(
            "function",
            new OpenAiToolFunction(
                tool.Name,
                tool.Description,
                tool.InputSchema));
    }
}
