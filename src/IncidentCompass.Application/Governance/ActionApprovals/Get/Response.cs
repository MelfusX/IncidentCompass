using System.Text.Json;

namespace IncidentCompass.Application.Governance.ActionApprovals.Get;

public sealed record ActionApprovalDetailsResponse(
    Guid Id,
    string Status,
    int ApprovalContractVersion,
    Guid OriginReportId,
    string ToolId,
    string Category,
    string Mode,
    string LogicalTargetId,
    JsonElement CanonicalPayload,
    string PayloadSha256,
    string ProvenanceSha256,
    string ApprovalSha256,
    string ReviewSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? DecisionAtUtc,
    string? RejectionReason,
    DateTimeOffset? CompletedAtUtc,
    string? ResultSummary,
    string? FailureCode,
    IReadOnlyList<ActionApprovalProvenanceResponse> Provenance);
