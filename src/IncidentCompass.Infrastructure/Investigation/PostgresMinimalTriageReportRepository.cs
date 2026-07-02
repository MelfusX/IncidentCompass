using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresMinimalTriageReportRepository(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider) : IMinimalTriageReportRepository
{
    public async Task<Guid> PublishAsync(
        TriageJob job,
        string workerId,
        MinimalTriageReport report,
        CancellationToken cancellationToken)
    {
        var reportId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var updated = await MarkJobSucceededAsync(connection, transaction, job, workerId, now, cancellationToken);
            if (!updated)
            {
                throw new InvalidOperationException($"Triage job '{job.Id}' could not be completed for attempt {job.Attempt}.");
            }

            await UpsertReportAsync(connection, transaction, reportId, job, report, now, cancellationToken);
            await MarkFaultTerminalAsync(connection, transaction, job.FaultId, report.Status, now, cancellationToken);
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
              AND status = 'Processing';
            """, connection, transaction);
        AddParameter(command, "now", now);
        AddParameter(command, "job_id", job.Id);
        AddParameter(command, "attempt", job.Attempt);
        AddParameter(command, "worker_id", workerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task UpsertReportAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reportId,
        TriageJob job,
        MinimalTriageReport report,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_reports (
                id, fault_id, status, summary, classification, confidence, is_mass_issue,
                recommended_next_action, limitations, config_hash, created_at_utc)
            VALUES (
                @id, @fault_id, @status, @summary, @classification, @confidence, NULL,
                @recommended_next_action, @limitations, @config_hash, @created_at_utc)
            ON CONFLICT (fault_id) DO UPDATE
            SET id = EXCLUDED.id,
                status = EXCLUDED.status,
                summary = EXCLUDED.summary,
                classification = EXCLUDED.classification,
                confidence = EXCLUDED.confidence,
                is_mass_issue = EXCLUDED.is_mass_issue,
                recommended_next_action = EXCLUDED.recommended_next_action,
                limitations = EXCLUDED.limitations,
                config_hash = EXCLUDED.config_hash,
                created_at_utc = EXCLUDED.created_at_utc;
            """, connection, transaction);

        AddParameter(command, "id", reportId);
        AddParameter(command, "fault_id", job.FaultId);
        AddParameter(command, "status", report.Status.ToString());
        AddParameter(command, "summary", report.Summary);
        AddParameter(command, "classification", report.Classification);
        AddParameter(command, "confidence", report.Confidence);
        AddParameter(command, "recommended_next_action", report.RecommendedNextAction);
        command.Parameters.AddWithValue("limitations", report.Limitations.ToArray());
        AddParameter(command, "config_hash", job.ConfigHash);
        AddParameter(command, "created_at_utc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkFaultTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        MinimalTriageReportStatus status,
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
        AddParameter(command, "status", status == MinimalTriageReportStatus.InsufficientEvidence ? "InsufficientEvidence" : "Completed");
        AddParameter(command, "now", now);
        AddParameter(command, "fault_id", faultId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
