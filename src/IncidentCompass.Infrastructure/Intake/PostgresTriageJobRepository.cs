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

        AddParameter(command, "id", id);
        AddParameter(command, "fault_id", faultId);
        AddParameter(command, "config_hash", configHash);
        AddParameter(command, "now", now);

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

        AddParameter(command, "fault_id", faultId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTriageJob(reader) : null;
    }

    private static TriageJob MapTriageJob(NpgsqlDataReader reader)
    {
        return new TriageJob(
            Id: reader.GetGuid(0),
            FaultId: reader.GetGuid(1),
            Status: Enum.Parse<TriageJobStatus>(reader.GetString(2)),
            Attempt: reader.GetInt32(3),
            LockedBy: reader.IsDBNull(4) ? null : reader.GetString(4),
            LockedUntilUtc: reader.IsDBNull(5) ? null : GetDateTimeOffset(reader, 5),
            NextAttemptAtUtc: reader.IsDBNull(6) ? null : GetDateTimeOffset(reader, 6),
            LastErrorCode: reader.IsDBNull(7) ? null : reader.GetString(7),
            LastErrorMessage: reader.IsDBNull(8) ? null : reader.GetString(8),
            ConfigHash: reader.GetString(9),
            CreatedAtUtc: GetDateTimeOffset(reader, 10),
            UpdatedAtUtc: GetDateTimeOffset(reader, 11));
    }

    // Matches PostgresObservabilityRepository's precedent: read timestamptz columns via
    // GetDateTime (never GetFieldValue<DateTimeOffset> directly) and convert explicitly, since
    // the column is always UTC-normalized by Postgres regardless of the CLR DateTime.Kind Npgsql
    // returns it with.
    private static DateTimeOffset GetDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
