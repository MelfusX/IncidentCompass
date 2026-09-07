using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.IntegrationTests;

internal sealed class ProposalOnlyExternalActionTool(
    ActionCategory category,
    bool oversized = false,
    string toolId = "action_test") : IExternalActionTool
{
    public int ExecutionCalls { get; private set; }

    public ActionCategory Category { get; } = category;
    public string LogicalTargetId => "test:target";
    public string AdapterBindingFingerprint { get; } = ExternalActionBinding.ComputeFingerprint(
        "synthetic", "test:target", "https://api.example.test", "resource-1");
    public AiToolDefinition Definition { get; } = new(
        toolId, "Synthetic post-report action.", "v1",
        CanonicalJsonSerializer.ToElement(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["message"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("message")
        }));

    public ToolValidationResult Validate(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            arguments.EnumerateObject().Any(property => property.Name != "message") ||
            !arguments.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(message.GetString()))
        {
            return ToolValidationResult.Invalid("invalid_arguments", "A message is required.");
        }

        return ToolValidationResult.Valid(JsonSerializer.SerializeToElement(new
        {
            message = message.GetString()!.Trim()
        }));
    }

    public ExternalActionPreparation Prepare(JsonElement sanitizedArguments)
    {
        if (oversized)
        {
            return new ExternalActionPreparation(
                Encoding.UTF8.GetBytes("{\"message\":\"" + new string('x', 70_000) + "\"}"),
                "Oversized synthetic proposal.");
        }

        var payload = new JsonObject
        {
            ["message"] = sanitizedArguments.GetProperty("message").GetString()
        };
        return new ExternalActionPreparation(
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            "Send a synthetic bounded notification.");
    }

    public Task<ExternalActionExecutionResult> ExecuteAsync(
        Guid actionId,
        ReadOnlyMemory<byte> canonicalPayload,
        CancellationToken cancellationToken)
    {
        ExecutionCalls++;
        throw new InvalidOperationException("Proposal creation invoked the external adapter.");
    }
}
