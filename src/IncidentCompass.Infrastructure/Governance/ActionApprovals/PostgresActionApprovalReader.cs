using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionApprovalReader
{
    public const string Columns = """
        a.id, a.tenant_id, a.origin_report_id, a.fault_id, a.job_id, a.attempt,
        a.tool_id, a.proposal_key, a.category, a.mode, a.logical_target_id,
        a.adapter_binding_fingerprint, a.approval_contract_version, a.provenance_sha256,
        a.state, a.canonical_payload, a.payload_sha256, a.approval_sha256,
        a.proposal_artifact_id, a.review_summary,
        (SELECT count(*)::integer FROM incidentcompass.action_approval_provenance p WHERE p.action_id = a.id),
        a.created_at_utc, a.expires_at_utc, a.decision_actor, a.decision_at_utc,
        a.rejection_reason, a.dispatch_owner, a.dispatch_fence, a.dispatch_started_at,
        a.dispatch_deadline_at, a.result_payload, a.result_summary, a.failure_code, a.completed_at_utc,
        a.external_resource_kind, a.external_resource_id,
        a.external_before_state, a.external_after_state
        """ + "\n";

    public static ActionApprovalRecord Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetGuid(3),
        reader.GetGuid(4),
        reader.GetInt32(5),
        reader.GetString(6),
        reader.GetString(7),
        ActionApprovalVocabulary.ParseCategory(reader.GetString(8)),
        ActionApprovalVocabulary.ParseMode(reader.GetString(9)),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetInt32(12),
        reader.GetString(13),
        ActionApprovalVocabulary.ParseState(reader.GetString(14)),
        reader.GetFieldValue<byte[]>(15),
        reader.GetString(16),
        reader.GetString(17),
        reader.GetGuid(18),
        reader.GetString(19),
        reader.GetInt32(20),
        reader.GetDateTimeOffset(21),
        reader.GetDateTimeOffset(22),
        reader.IsDBNull(23) ? null : reader.GetString(23),
        reader.IsDBNull(24) ? null : reader.GetDateTimeOffset(24),
        reader.IsDBNull(25) ? null : reader.GetString(25),
        reader.IsDBNull(26) ? null : reader.GetString(26),
        reader.IsDBNull(27) ? null : reader.GetGuid(27),
        reader.IsDBNull(28) ? null : reader.GetDateTimeOffset(28),
        reader.IsDBNull(29) ? null : reader.GetDateTimeOffset(29),
        reader.IsDBNull(30) ? null : reader.GetFieldValue<byte[]>(30),
        reader.IsDBNull(31) ? null : reader.GetString(31),
        reader.IsDBNull(32) ? null : reader.GetString(32),
        reader.IsDBNull(33) ? null : reader.GetDateTimeOffset(33),
        ReadAuditProjection(reader));

    private static ExternalActionAuditProjection? ReadAuditProjection(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(34))
        {
            return null;
        }

        var projection = new ExternalActionAuditProjection(
            reader.GetString(34),
            reader.GetString(35),
            reader.GetString(36),
            reader.GetString(37));
        projection.Validate();
        return projection;
    }
}
