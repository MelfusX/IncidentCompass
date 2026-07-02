using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresFaultRepository(PostgresDataSourceProvider dataSourceProvider, PostgresIntakeTransactionContext transactionContext) : IFaultRepository
{
    private const string SelectColumns = """
        id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version, fingerprint_strength,
        can_group, service_name, environment, severity, correlation_id, created_at_utc, completed_at_utc,
        recurrence_of
        """;

    public async Task<Fault?> FindOpenFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT {SelectColumns}
            FROM incidentcompass.faults
            WHERE tenant_id = @tenant_id
              AND service_name = @service_name
              AND environment = @environment
              AND fingerprint = @fingerprint
              AND fingerprint_version = @fingerprint_version
              AND status IN ('Queued', 'Analyzing')
            LIMIT 1;
            """, lease.Connection, lease.Transaction);

        AddGroupKeyParameters(command, tenantId, serviceName, environment, fingerprint, fingerprintVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFault(reader) : null;
    }

    public async Task<Fault?> FindMostRecentClosedFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT {SelectColumns}
            FROM incidentcompass.faults
            WHERE tenant_id = @tenant_id
              AND service_name = @service_name
              AND environment = @environment
              AND fingerprint = @fingerprint
              AND fingerprint_version = @fingerprint_version
              AND status IN ('Completed', 'Failed', 'InsufficientEvidence')
            ORDER BY created_at_utc DESC
            LIMIT 1;
            """, lease.Connection, lease.Transaction);

        AddGroupKeyParameters(command, tenantId, serviceName, environment, fingerprint, fingerprintVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFault(reader) : null;
    }

    public async Task<Fault?> TryInsertAsync(Fault fault, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.faults (
                id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version, fingerprint_strength,
                service_name, environment, severity, correlation_id, created_at_utc, completed_at_utc, recurrence_of)
            VALUES (
                @id, @trigger_signal_id, @tenant_id, @status, @fingerprint, @fingerprint_version, @fingerprint_strength,
                @service_name, @environment, @severity, @correlation_id, @created_at_utc, @completed_at_utc, @recurrence_of)
            ON CONFLICT (tenant_id, service_name, environment, fingerprint, fingerprint_version)
                WHERE status IN ('Queued', 'Analyzing') AND can_group
                DO NOTHING
            RETURNING id;
            """, lease.Connection, lease.Transaction);

        AddParameter(command, "id", fault.Id);
        AddParameter(command, "trigger_signal_id", fault.TriggerSignalId);
        AddParameter(command, "tenant_id", fault.TenantId);
        AddParameter(command, "status", fault.Status.ToString());
        AddParameter(command, "fingerprint", fault.Fingerprint);
        AddParameter(command, "fingerprint_version", fault.FingerprintVersion);
        AddParameter(command, "fingerprint_strength", ToDbString(fault.FingerprintStrength));
        AddParameter(command, "service_name", fault.ServiceName);
        AddParameter(command, "environment", fault.Environment);
        AddParameter(command, "severity", fault.Severity);
        AddParameter(command, "correlation_id", fault.CorrelationId);
        AddParameter(command, "created_at_utc", fault.CreatedAtUtc);
        AddParameter(command, "completed_at_utc", fault.CompletedAtUtc);
        AddParameter(command, "recurrence_of", fault.RecurrenceOf);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var inserted = await reader.ReadAsync(cancellationToken);
        return inserted ? fault : null;
    }

    public async Task<Fault?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT {SelectColumns}
            FROM incidentcompass.faults
            WHERE id = @id
            LIMIT 1;
            """, lease.Connection, lease.Transaction);

        AddParameter(command, "id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFault(reader) : null;
    }

    private static void AddGroupKeyParameters(
        NpgsqlCommand command,
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion)
    {
        AddParameter(command, "tenant_id", tenantId);
        AddParameter(command, "service_name", serviceName);
        AddParameter(command, "environment", environment);
        AddParameter(command, "fingerprint", fingerprint);
        AddParameter(command, "fingerprint_version", fingerprintVersion);
    }

    private static Fault MapFault(NpgsqlDataReader reader)
    {
        return new Fault(
            Id: reader.GetGuid(0),
            TriggerSignalId: reader.GetGuid(1),
            TenantId: reader.GetString(2),
            Status: Enum.Parse<FaultStatus>(reader.GetString(3)),
            Fingerprint: reader.GetString(4),
            FingerprintVersion: reader.GetInt32(5),
            FingerprintStrength: Enum.Parse<FingerprintStrength>(reader.GetString(6), ignoreCase: true),
            CanGroup: reader.GetBoolean(7),
            ServiceName: reader.GetString(8),
            Environment: reader.GetString(9),
            Severity: reader.IsDBNull(10) ? null : reader.GetString(10),
            CorrelationId: reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAtUtc: GetDateTimeOffset(reader, 12),
            CompletedAtUtc: reader.IsDBNull(13) ? null : GetDateTimeOffset(reader, 13),
            RecurrenceOf: reader.IsDBNull(14) ? null : reader.GetGuid(14));
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

    private static string ToDbString(FingerprintStrength strength) => strength.ToString().ToLowerInvariant();

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
