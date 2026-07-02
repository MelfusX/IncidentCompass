using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageJobRuntimeRepository(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider) : ITriageJobRuntimeRepository
{
    public async Task<TriageJob?> ClaimNextAsync(
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

    public async Task RecordAttemptFailureAsync(
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        CancellationToken cancellationToken)
    {
        if (failure.Status is not (TriageJobStatus.RetryPending or TriageJobStatus.DeadLettered))
        {
            throw new ArgumentException("Attempt failure status must be RetryPending or DeadLettered.", nameof(failure));
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var updated = await RecordFailureCoreAsync(connection, transaction, job, workerId, failure, now, cancellationToken);
            if (updated && failure.Status == TriageJobStatus.DeadLettered)
            {
                await MarkFaultFailedAsync(connection, transaction, job.FaultId, now, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
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
                attempt = CASE WHEN job.status = 'Pending' THEN job.attempt ELSE job.attempt + 1 END,
                locked_by = @worker_id,
                locked_until_utc = @locked_until_utc,
                next_attempt_at_utc = NULL,
                updated_at_utc = @now
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING job.id, job.fault_id, job.status, job.attempt, job.locked_by,
                      job.locked_until_utc, job.next_attempt_at_utc, job.last_error_code,
                      job.last_error_message, job.config_hash, job.created_at_utc, job.updated_at_utc;
            """, connection, transaction);

        AddParameter(command, "worker_id", workerId);
        AddParameter(command, "locked_until_utc", lockedUntilUtc);
        AddParameter(command, "now", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? PostgresTriageJobMapper.Map(reader)
            : null;
    }

    private static async Task<bool> RecordFailureCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.triage_jobs
            SET status = @status,
                locked_by = NULL,
                locked_until_utc = NULL,
                next_attempt_at_utc = @next_attempt_at_utc,
                last_error_code = @last_error_code,
                last_error_message = @last_error_message,
                updated_at_utc = @now
            WHERE id = @id
              AND attempt = @attempt
              AND locked_by = @worker_id
              AND status = 'Processing';
            """, connection, transaction);

        AddParameter(command, "status", failure.Status.ToString());
        AddParameter(command, "next_attempt_at_utc", failure.NextAttemptAtUtc);
        AddParameter(command, "last_error_code", failure.ErrorCode);
        AddParameter(command, "last_error_message", failure.ErrorMessage);
        AddParameter(command, "now", now);
        AddParameter(command, "id", job.Id);
        AddParameter(command, "attempt", job.Attempt);
        AddParameter(command, "worker_id", workerId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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
        AddParameter(command, "fault_id", faultId);
        AddParameter(command, "now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkFaultFailedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.faults
            SET status = 'Failed',
                completed_at_utc = @now
            WHERE id = @fault_id
              AND status IN ('Queued', 'Analyzing');
            """, connection, transaction);
        AddParameter(command, "fault_id", faultId);
        AddParameter(command, "now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
