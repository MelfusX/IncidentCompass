using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal static class PostgresReportPublishedEventWriter
{
    public static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        Guid reportId,
        string summary,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                decision, decision_reason, tool_status, tokens_delta, workers_delta,
                payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, @attempt, 'ReportPublished', NULL, 'publish_report', @rationale,
                NULL, NULL, NULL, NULL, NULL, @payload_ref, @config_hash, @created_at_utc);
            """, connection, transaction);
        command.AddParameter("fault_id", job.FaultId);
        command.AddParameter("job_id", job.Id);
        command.AddParameter("attempt", job.Attempt);
        command.AddParameter("rationale", summary);
        command.AddParameter("payload_ref", "report:" + reportId);
        command.AddParameter("config_hash", job.ConfigHash);
        command.AddParameter("created_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
