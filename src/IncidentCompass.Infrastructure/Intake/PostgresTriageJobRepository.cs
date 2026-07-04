using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageJobRepository(PostgresDataSourceProvider dataSourceProvider, PostgresIntakeTransactionContext transactionContext, TimeProvider timeProvider)
    : ITriageJobRepository
{
    public async Task<TriageJob> InsertPendingAsync(Guid faultId, string configHash, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_jobs (id, fault_id, status, attempt, config_hash, created_at_utc, updated_at_utc)
            VALUES (@id, @fault_id, 'Pending', 1, @config_hash, @now, @now);
            """, lease.Connection, lease.Transaction);

        command.AddParameter("id", id);
        command.AddParameter("fault_id", faultId);
        command.AddParameter("config_hash", configHash);
        command.AddParameter("now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new TriageJob(
            Id: id,
            FaultId: faultId,
            Status: TriageJobStatus.Pending,
            Attempt: 1,
            LockedBy: null,
            LockedUntilUtc: null,
            NextAttemptAtUtc: null,
            LastErrorCode: null,
            LastErrorMessage: null,
            ConfigHash: configHash,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
    }

    public async Task<TriageJob?> FindByFaultIdAsync(Guid faultId, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, fault_id, status, attempt, locked_by, locked_until_utc, next_attempt_at_utc,
                   last_error_code, last_error_message, config_hash, created_at_utc, updated_at_utc
            FROM incidentcompass.triage_jobs
            WHERE fault_id = @fault_id
            ORDER BY created_at_utc DESC
            LIMIT 1;
            """, lease.Connection, lease.Transaction);

        command.AddParameter("fault_id", faultId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresTriageJobMapper.Map(reader) : null;
    }
}
