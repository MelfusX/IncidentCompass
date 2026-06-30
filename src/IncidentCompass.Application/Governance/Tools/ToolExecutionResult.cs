using IncidentCompass.Domain.Governance;
using System.Text.Json;

namespace IncidentCompass.Application.Governance.Tools;

public sealed record ToolExecutionResult(
    ToolExecutionStatus Status,
    JsonElement Output,
    string? ErrorCode = null,
    string? ErrorMessage = null);
