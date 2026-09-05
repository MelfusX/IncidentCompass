namespace IncidentCompass.Api;

internal sealed record ApproveActionRequest(string PayloadSha256, string ApprovalSha256);
