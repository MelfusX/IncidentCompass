using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Governance;
using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;

namespace IncidentCompass.Application.Governance.Tools;

public interface IAgentTool
{
    AiToolDefinition Definition { get; }

    ToolPolicyMetadata Policy { get; }

    ToolValidationResult Validate(JsonElement arguments);

    Task<ToolExecutionResult> ExecuteAsync(
        JsonElement sanitizedArguments,
        CancellationToken cancellationToken);
}
