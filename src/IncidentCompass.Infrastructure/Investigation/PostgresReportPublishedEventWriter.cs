using IncidentCompass.Domain.Incidents;
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
        AddParameter(command, "fault_id", job.FaultId);
        AddParameter(command, "job_id", job.Id);
        AddParameter(command, "attempt", job.Attempt);
        AddParameter(command, "rationale", summary);
        AddParameter(command, "payload_ref", "report:" + reportId);
        AddParameter(command, "config_hash", job.ConfigHash);
        AddParameter(command, "created_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
