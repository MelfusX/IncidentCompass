using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageJobRuntimeRepository(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider,
    PostgresTriageJobLeaseStore leaseStore,
    PostgresTriageJobAttemptFailureStore attemptFailureStore) : ITriageJobRuntimeRepository
{
    public Task<TriageJob?> ClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "claim triage job",
            () => ClaimNextTransactionAsync(workerId, leaseDuration, cancellationToken));

    public Task<bool> RenewLeaseAsync(
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        leaseStore.RenewAsync(job, workerId, leaseDuration, cancellationToken);

    public Task RecordAttemptFailureAsync(
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        CancellationToken cancellationToken) =>
        attemptFailureStore.RecordAsync(job, workerId, failure, cancellationToken);

    private async Task<TriageJob?> ClaimNextTransactionAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var lockedUntilUtc = now.Add(leaseDuration);
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var claimed = await ClaimNextCoreAsync(connection, transaction, workerId, now, lockedUntilUtc, cancellationToken);
            if (claimed is not null)
            {
                await MarkFaultAnalyzingAsync(connection, transaction, claimed.FaultId, now, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<TriageJob?> ClaimNextCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string workerId,
        DateTimeOffset now,
        DateTimeOffset lockedUntilUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT id
                FROM incidentcompass.triage_jobs
                WHERE status = 'Pending'
                   OR (status = 'RetryPending' AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= @now))
                   OR (status = 'Processing' AND locked_until_utc IS NOT NULL AND locked_until_utc <= @now)
                ORDER BY created_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE incidentcompass.triage_jobs AS job
            SET status = 'Processing',
                attempt = CASE
                    WHEN job.status = 'Pending'
                        OR (job.status = 'RetryPending' AND job.retry_without_consuming_attempt)
                        THEN job.attempt
                    ELSE job.attempt + 1
                END,
                locked_by = @worker_id,
                locked_until_utc = @locked_until_utc,
                next_attempt_at_utc = NULL,
                retry_without_consuming_attempt = false,
                last_error_code = CASE
                    WHEN job.status = 'RetryPending' AND job.retry_without_consuming_attempt THEN NULL
                    ELSE job.last_error_code
                END,
                last_error_message = CASE
                    WHEN job.status = 'RetryPending' AND job.retry_without_consuming_attempt THEN NULL
                    ELSE job.last_error_message
                END,
                updated_at_utc = @now
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING job.id, job.fault_id, job.status, job.attempt, job.locked_by,
                      job.locked_until_utc, job.next_attempt_at_utc, job.last_error_code,
                      job.last_error_message, job.config_hash, job.created_at_utc, job.updated_at_utc,
                      job.retriage_trigger_job_id, job.supersedes_report_id;
            """, connection, transaction);
        command.AddParameter("worker_id", workerId);
        command.AddParameter("locked_until_utc", lockedUntilUtc);
        command.AddParameter("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? PostgresTriageJobMapper.Map(reader)
            : null;
    }

    private static async Task MarkFaultAnalyzingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.faults
            SET status = 'Analyzing'
            WHERE id = @fault_id
              AND status = 'Queued';
            """, connection, transaction);
        command.AddParameter("fault_id", faultId);
        command.AddParameter("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
