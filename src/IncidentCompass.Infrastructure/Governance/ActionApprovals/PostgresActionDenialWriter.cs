using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionDenialWriter
{
    public static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GroundedActionProposalContext origin,
        Guid reportId,
        string? auditedToolId,
        string reasonCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                decision, decision_reason, tool_status, tokens_delta, workers_delta,
                payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, @attempt, 'PolicyDecision', NULL, @tool_name, NULL,
                @decision, @decision_reason, NULL, NULL, NULL,
                @payload_ref, @config_hash, @created_at_utc);
            """, connection, transaction);
        command.AddParameter("fault_id", origin.FaultId);
        command.AddParameter("job_id", origin.JobId);
        command.AddParameter("attempt", origin.Attempt);
        command.AddParameter("tool_name", auditedToolId);
        command.AddParameter("decision", TriageLedgerDecision.Denied.ToDbString());
        command.AddParameter("decision_reason", reasonCode);
        command.AddParameter("payload_ref", "report:" + reportId);
        command.AddParameter("config_hash", origin.ConfigHash);
        command.AddParameter("created_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
