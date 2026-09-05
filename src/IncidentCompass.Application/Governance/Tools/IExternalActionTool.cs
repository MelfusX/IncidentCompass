using System.Text.Json;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.Tools;

public interface IExternalActionTool : IAgentTool
{
    ActionCategory Category { get; }

    string LogicalTargetId { get; }

    string AdapterBindingFingerprint { get; }

    ExternalActionPreparation Prepare(JsonElement sanitizedArguments);

    Task<ExternalActionExecutionResult> ExecuteAsync(
        Guid actionId,
        ReadOnlyMemory<byte> canonicalPayload,
        CancellationToken cancellationToken);
}
