using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresTriageReportRepository(
    PostgresDataSourceProvider dataSourceProvider,
    ITriageReportFinalCommitFaultInjector faultInjector,
    TimeProvider timeProvider) : ITriageReportRepository
{
    private readonly PostgresReportEvidenceGrounder evidenceGrounder = new();

    public async Task<Guid> PublishAsync(
        TriageJob job,
        string workerId,
        TriageReport report,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var evidence = await evidenceGrounder.GroundAsync(connection, transaction, job, report.Evidence, cancellationToken);
            var isMassIssue = await ReadIsMassIssueAsync(connection, transaction, job.Id, cancellationToken);
            var updated = await MarkJobSucceededAsync(connection, transaction, job, workerId, now, cancellationToken);
            if (!updated)
            {
                throw new InvalidOperationException($"Triage job '{job.Id}' could not be completed for attempt {job.Attempt}.");
            }

            var reportId = await UpsertReportAsync(connection, transaction, job, report, isMassIssue, now, cancellationToken);
            await PostgresTriageEvidenceWriter.ReplaceAsync(connection, transaction, reportId, evidence, now, cancellationToken);
            await MarkFaultTerminalAsync(connection, transaction, job.FaultId, report.Status, now, cancellationToken);
            await faultInjector.BeforeReportPublishedLedgerEventAsync(cancellationToken);
            await InsertReportPublishedEventAsync(connection, transaction, job, reportId, report.Summary, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reportId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<bool> MarkJobSucceededAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.triage_jobs
            SET status = 'Succeeded',
                locked_by = NULL,
                locked_until_utc = NULL,
                next_attempt_at_utc = NULL,
                updated_at_utc = @now
            WHERE id = @job_id
              AND attempt = @attempt
              AND locked_by = @worker_id
              AND status = 'Processing'
              AND status <> 'Succeeded';
            """, connection, transaction);
        AddParameter(command, "now", now);
        AddParameter(command, "job_id", job.Id);
        AddParameter(command, "attempt", job.Attempt);
        AddParameter(command, "worker_id", workerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool?> ReadIsMassIssueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT CASE
                       WHEN jsonb_typeof(redacted_payload->'isMassIssue') = 'boolean'
                       THEN (redacted_payload->>'isMassIssue')::boolean
                       ELSE NULL
                   END
            FROM incidentcompass.triage_artifacts
            WHERE job_id = @job_id
              AND attempt IS NULL
              AND kind = 'NeighborSet'
            ORDER BY created_at_utc DESC, id DESC
            LIMIT 1;
            """, connection, transaction);
        AddParameter(command, "job_id", jobId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull or null ? null : (bool)value;
    }

    private static async Task<Guid> UpsertReportAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        TriageReport report,
        bool? isMassIssue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_reports (
                id, fault_id, status, summary, classification, confidence, is_mass_issue,
                recommended_next_action, limitations, config_hash, created_at_utc)
            VALUES (
                @id, @fault_id, @status, @summary, @classification, @confidence, @is_mass_issue,
                @recommended_next_action, @limitations, @config_hash, @created_at_utc)
            ON CONFLICT (fault_id) DO UPDATE
            SET status = EXCLUDED.status,
                summary = EXCLUDED.summary,
                classification = EXCLUDED.classification,
                confidence = EXCLUDED.confidence,
                is_mass_issue = EXCLUDED.is_mass_issue,
                recommended_next_action = EXCLUDED.recommended_next_action,
                limitations = EXCLUDED.limitations,
                config_hash = EXCLUDED.config_hash,
                created_at_utc = EXCLUDED.created_at_utc
            RETURNING id;
            """, connection, transaction);
        AddParameter(command, "id", Guid.NewGuid());
        AddParameter(command, "fault_id", job.FaultId);
        AddParameter(command, "status", report.Status.ToString());
        AddParameter(command, "summary", report.Summary);
        AddParameter(command, "classification", report.Classification);
        AddParameter(command, "confidence", report.Confidence);
        AddParameter(command, "is_mass_issue", isMassIssue);
        AddParameter(command, "recommended_next_action", report.RecommendedNextAction);
        command.Parameters.AddWithValue("limitations", report.Limitations.ToArray());
        AddParameter(command, "config_hash", job.ConfigHash);
        AddParameter(command, "created_at_utc", now);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task MarkFaultTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        TriageReportStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.faults
            SET status = @status,
                completed_at_utc = @now
            WHERE id = @fault_id
              AND status IN ('Queued', 'Analyzing');
            """, connection, transaction);
        AddParameter(command, "status", status == TriageReportStatus.InsufficientEvidence ? "InsufficientEvidence" : "Completed");
        AddParameter(command, "now", now);
        AddParameter(command, "fault_id", faultId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReportPublishedEventAsync(
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
