using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Governance;
using System.Text.Json;

namespace IncidentCompass.Application.Governance.Tools;

public interface IAgentTool
{
    AiToolDefinition Definition { get; }

    ToolPolicyMetadata Policy { get; }

    ToolValidationResult Validate(JsonElement arguments);

    Task<ToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement sanitizedArguments,
        CancellationToken cancellationToken);
}
