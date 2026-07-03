using IncidentCompass.Domain.Governance;
using IncidentCompass.Domain.Incidents;
using System.Text.Json;

namespace IncidentCompass.Application.Governance.Tools;

public sealed record ToolExecutionResult(
    ToolExecutionStatus Status,
    JsonElement Output,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyCollection<TriageArtifact>? Artifacts = null);
