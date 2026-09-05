using System.Text.Json;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Governance.Validation;

namespace IncidentCompass.Application.Governance.Tools;

public interface IAgentTool
{
    AiToolDefinition Definition { get; }

    ToolValidationResult Validate(JsonElement arguments);
}
