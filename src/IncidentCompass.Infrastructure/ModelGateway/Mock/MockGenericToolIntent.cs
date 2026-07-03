using IncidentCompass.Application.Core.ModelClients;
using System.Text.RegularExpressions;

namespace IncidentCompass.Infrastructure.ModelGateway.Mock;

internal static partial class MockGenericToolIntent
{
    public static IReadOnlyList<AiToolCall> ProposeToolCalls(string message)
    {
        // Regex intent detection is for deterministic local demos/tests, not production tool selection.
        if (HasProfileIntent(message))
        {
            return [MockAiModelResponseFactory.ToolCall("mock-tool-1", "GetCurrentUserProfile", "{}")];
        }

        if (HasSupportTicketIntent(message))
        {
            return
            [
                MockAiModelResponseFactory.ToolCall("mock-tool-1", "CreateSupportTicket",
                    """{"title":"Demo support ticket","description":"Created by the mock agent.","priority":"normal"}""")
            ];
        }

        if (HasDraftEmailIntent(message))
        {
            return
            [
                MockAiModelResponseFactory.ToolCall("mock-tool-1", "DraftEmail",
                    """{"to":"demo@example.test","subject":"Demo draft","body":"This is a draft created by the mock agent."}""")
            ];
        }

        if (HasDeleteDocumentIntent(message))
        {
            return [MockAiModelResponseFactory.ToolCall("mock-tool-1", "DeleteDocument", """{"documentId":"00000000-0000-0000-0000-000000000000"}""")];
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
}
