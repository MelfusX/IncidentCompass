using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageJobAttemptFailureStore(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider)
{
    public Task RecordAsync(
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "record triage attempt failure",
            () => RecordTransactionAsync(job, workerId, failure, cancellationToken));

    private async Task RecordTransactionAsync(
        TriageJob job,
        string workerId,
        TriageJobAttemptFailure failure,
        CancellationToken cancellationToken)
    {
        ValidateFailure(failure);

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

    private static void ValidateFailure(TriageJobAttemptFailure failure)
    {
        if (failure.Status is not (TriageJobStatus.RetryPending or TriageJobStatus.DeadLettered))
        {
            throw new ArgumentException("Attempt failure status must be RetryPending or DeadLettered.", nameof(failure));
        }

        if (failure.RetryBudgetDisposition is not (
                TriageJobRetryBudgetDisposition.ConsumeAttempt or
                TriageJobRetryBudgetDisposition.DoNotConsumeAttempt))
        {
            throw new ArgumentException("Attempt failure retry-budget disposition is invalid.", nameof(failure));
        }

        if (failure.RetryBudgetDisposition == TriageJobRetryBudgetDisposition.DoNotConsumeAttempt &&
            failure.Status != TriageJobStatus.RetryPending)
        {
            throw new ArgumentException("Only RetryPending failures may avoid consuming an attempt.", nameof(failure));
        }
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
                retry_without_consuming_attempt = @retry_without_consuming_attempt,
                last_error_code = @last_error_code,
                last_error_message = @last_error_message,
                updated_at_utc = @now
            WHERE id = @id
              AND attempt = @attempt
              AND locked_by = @worker_id
              AND locked_until_utc > @now
              AND status = 'Processing';
            """, connection, transaction);
        command.AddParameter("status", failure.Status.ToDbString());
        command.AddParameter("next_attempt_at_utc", failure.NextAttemptAtUtc);
        command.AddParameter(
            "retry_without_consuming_attempt",
            failure.RetryBudgetDisposition == TriageJobRetryBudgetDisposition.DoNotConsumeAttempt);
        command.AddParameter("last_error_code", failure.ErrorCode);
        command.AddParameter("last_error_message", failure.ErrorMessage);
        command.AddParameter("now", now);
        command.AddParameter("id", job.Id);
        command.AddParameter("attempt", job.Attempt);
        command.AddParameter("worker_id", workerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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
        command.AddParameter("fault_id", faultId);
        command.AddParameter("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
