using System.Text.Json;

namespace IncidentCompass.Application.Governance.Tools;

public interface IImmediateAgentTool : IAgentTool
{
    Task<ToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement sanitizedArguments,
        CancellationToken cancellationToken);
}
