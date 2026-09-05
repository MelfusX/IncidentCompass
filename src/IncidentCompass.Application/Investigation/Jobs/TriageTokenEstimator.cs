using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class TriageTokenEstimator
{
    public static int EstimateMessages(
        IReadOnlyCollection<AiChatMessage> messages,
        IReadOnlyCollection<AiToolDefinition>? tools = null)
    {
        var characters = messages.Sum(static message => message.Content.Length);
        if (tools is not null)
        {
            characters += tools.Sum(static tool =>
                tool.Name.Length + tool.Description.Length + tool.InputSchema.GetRawText().Length);
        }

        return EstimateTextLength(characters);
    }

    public static int EstimateText(string? text)
    {
        return EstimateTextLength(text?.Length ?? 0);
    }

    private static int EstimateTextLength(int characters)
    {
        return Math.Max(1, (characters + 3) / 4);
    }
}
