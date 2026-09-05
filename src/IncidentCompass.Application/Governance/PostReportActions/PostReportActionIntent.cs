using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.Application.Governance.PostReportActions;

public sealed record PostReportActionIntent(
    Guid Id,
    string TenantId,
    Guid OriginReportId,
    Guid FaultId,
    Guid JobId,
    int Attempt,
    string ToolId,
    int WorkflowVersion,
    string? RouteId,
    string ConfigHash,
    string ProposalKey,
    byte[] WorkflowInput,
    PostReportActionIntentState State,
    string? ClaimOwner,
    Guid? ClaimFence,
    DateTimeOffset? ClaimUntilUtc,
    int AttemptCount,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public bool HasValidCanonicalInput()
    {
        if (WorkflowInput is not { Length: >= 1 and <= 8192 })
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(WorkflowInput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Any(static property =>
                    property.Name is not ("originReportId" or "toolId" or "workflowVersion" or "routeId")) ||
                !root.TryGetProperty("originReportId", out var report) ||
                report.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(report.GetString(), "N", out var reportId) ||
                reportId != OriginReportId ||
                !root.TryGetProperty("toolId", out var tool) ||
                tool.ValueKind != JsonValueKind.String ||
                !string.Equals(tool.GetString(), ToolId, StringComparison.Ordinal) ||
                !root.TryGetProperty("workflowVersion", out var version) ||
                !version.TryGetInt32(out var parsedVersion) ||
                parsedVersion != WorkflowVersion)
            {
                return false;
            }

            var hasRoute = root.TryGetProperty("routeId", out var route);
            if (hasRoute != (RouteId is not null) ||
                (hasRoute && (route.ValueKind != JsonValueKind.String ||
                              !string.Equals(route.GetString(), RouteId, StringComparison.Ordinal))))
            {
                return false;
            }

            var canonical = Encoding.UTF8.GetBytes(
                CanonicalJsonSerializer.Canonicalize(JsonNode.Parse(WorkflowInput)));
            return WorkflowInput.AsSpan().SequenceEqual(canonical);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
