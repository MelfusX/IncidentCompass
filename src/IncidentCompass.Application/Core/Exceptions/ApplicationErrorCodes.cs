namespace IncidentCompass.Application.Core.Exceptions;

/// <summary>
/// Stable, machine-readable codes a throw site can attach to an <see cref="AppException" /> so
/// the API error boundary can report which resource or rule was involved without echoing the
/// exception's developer-facing message. Centralized here so the same code is not respelled at
/// each throw site.
/// </summary>
public static class ApplicationErrorCodes
{
    public const string FaultNotFound = "fault_not_found";
    public const string TriageReportNotFound = "triage_report_not_found";
    public const string ActionApprovalNotFound = "action_approval_not_found";
    public const string TriageConfigurationSnapshotNotFound = "triage_configuration_snapshot_not_found";

    public const string ActionProposalProvenanceConflict = "action_proposal_provenance_conflict";
    public const string ActionProposalInputConflict = "action_proposal_input_conflict";
    public const string ActionApprovalConflictCodePrefix = "action_approval_conflict_";

    public const string TenantContextRequired = "tenant_context_required";
    public const string ActionOperatorRequired = "action_operator_required";
}
