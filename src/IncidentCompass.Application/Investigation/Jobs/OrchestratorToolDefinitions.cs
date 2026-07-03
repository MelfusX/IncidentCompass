using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class OrchestratorToolDefinitions
{
    public static IReadOnlyList<AiToolDefinition> Create(TriageConfiguration configuration)
    {
        return [CreateDelegateTool(configuration.Roles.Keys), CreatePublishReportTool()];
    }

    private static AiToolDefinition CreateDelegateTool(IEnumerable<string> roles)
    {
        var roleEnum = new JsonArray();
        foreach (var role in roles.Order(StringComparer.Ordinal))
        {
            roleEnum.Add(role);
        }
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["role"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = roleEnum
                },
                ["task"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("role", "task")
        };

        return new AiToolDefinition(
            "delegate",
            "Delegate one bounded task to a configured worker role and wait for its result.",
            "v1",
            ToElement(schema));
    }

    private static AiToolDefinition CreatePublishReportTool()
    {
        var evidenceItemSchema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["properties"] = new JsonObject
            {
                ["referenceId"] = new JsonObject { ["type"] = "string" },
                ["quote"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("referenceId")
        };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["report_json"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true,
                    ["properties"] = new JsonObject
                    {
                        ["status"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Completed", "InsufficientEvidence") },
                        ["summary"] = new JsonObject { ["type"] = "string" },
                        ["classification"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("KnownIncident", "LikelyRegression", "SimpleKnownError", "Unknown", "Noise") },
                        ["confidence"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Low", "Medium", "High") },
                        ["evidence"] = new JsonObject { ["type"] = "array", ["items"] = evidenceItemSchema },
                        ["limitations"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                        ["recommendedNextAction"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("status", "summary", "classification", "confidence", "evidence", "limitations", "recommendedNextAction")
                }
            },
            ["required"] = new JsonArray("report_json")
        };

        return new AiToolDefinition(
            "publish_report",
            "Publish the final grounded triage report and end this investigation.",
            "v1",
            ToElement(schema));
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
