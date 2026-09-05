using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionLedgerWriter
{
    public static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GroundedActionProposalContext origin,
        TriageLedgerEventType eventType,
        string toolId,
        string? actor,
        string? rationale,
        TriageLedgerDecision? decision,
        TriageLedgerToolStatus? toolStatus,
        string payloadRef,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                decision, decision_reason, tool_status, tokens_delta, workers_delta,
                payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, @attempt, @event_type, @role, @tool_name, @rationale,
                @decision, NULL, @tool_status, NULL, NULL,
                @payload_ref, @config_hash, @created_at_utc);
            """, connection, transaction);
        command.AddParameter("fault_id", origin.FaultId);
        command.AddParameter("job_id", origin.JobId);
        command.AddParameter("attempt", origin.Attempt);
        command.AddParameter("event_type", eventType.ToDbString());
        command.AddParameter("role", actor);
        command.AddParameter("tool_name", toolId);
        command.AddParameter("rationale", rationale);
        command.AddParameter("decision", decision?.ToDbString());
        command.AddParameter("tool_status", toolStatus?.ToDbString());
        command.AddParameter("payload_ref", payloadRef);
        command.AddParameter("config_hash", origin.ConfigHash);
        command.AddParameter("created_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
