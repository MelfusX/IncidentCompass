namespace IncidentCompass.Api;

internal sealed record RejectActionRequest(string PayloadSha256, string ApprovalSha256, string? Reason);
