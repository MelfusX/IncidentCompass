using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresSignalRepository(PostgresDataSourceProvider dataSourceProvider, PostgresIntakeTransactionContext transactionContext) : ISignalRepository
{
    public async Task InsertAsync(Signal signal, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.signals (
                id, tenant_id, source, fault_id, fingerprint, fingerprint_version, fingerprint_strength,
                external_id, is_suppressed, suppressed_by_fault_id, suppression_reason,
                trace_id, span_id, parent_span_id, service_name, environment, operation_name,
                severity, error_type, error_message, summary, description,
                http_method, http_route, http_status_code, duration_ms, attributes, body,
                observed_at_utc, received_at_utc)
            VALUES (
                @id, @tenant_id, @source, @fault_id, @fingerprint, @fingerprint_version, @fingerprint_strength,
                @external_id, @is_suppressed, @suppressed_by_fault_id, @suppression_reason,
                @trace_id, @span_id, @parent_span_id, @service_name, @environment, @operation_name,
                @severity, @error_type, @error_message, @summary, @description,
                @http_method, @http_route, @http_status_code, @duration_ms, @attributes, @body,
                @observed_at_utc, @received_at_utc);
            """, lease.Connection, lease.Transaction);

        AddParameter(command, "id", signal.Id);
        AddParameter(command, "tenant_id", signal.TenantId);
        AddParameter(command, "source", signal.Source);
        AddParameter(command, "fault_id", signal.FaultId);
        AddParameter(command, "fingerprint", signal.Fingerprint);
        AddParameter(command, "fingerprint_version", signal.FingerprintVersion);
        AddParameter(command, "fingerprint_strength", ToDbString(signal.FingerprintStrength));
        AddParameter(command, "external_id", signal.ExternalId);
        AddParameter(command, "is_suppressed", signal.IsSuppressed);
        AddParameter(command, "suppressed_by_fault_id", signal.SuppressedByFaultId);
        AddParameter(command, "suppression_reason", signal.SuppressionReason);
        AddParameter(command, "trace_id", signal.TraceId);
        AddParameter(command, "span_id", signal.SpanId);
        AddParameter(command, "parent_span_id", signal.ParentSpanId);
        AddParameter(command, "service_name", signal.ServiceName);
        AddParameter(command, "environment", signal.Environment);
        AddParameter(command, "operation_name", signal.OperationName);
        AddParameter(command, "severity", signal.Severity);
        AddParameter(command, "error_type", signal.ErrorType);
        AddParameter(command, "error_message", signal.ErrorMessage);
        AddParameter(command, "summary", signal.Summary);
        AddParameter(command, "description", signal.Description);
        AddParameter(command, "http_method", signal.HttpMethod);
        AddParameter(command, "http_route", signal.HttpRoute);
        AddParameter(command, "http_status_code", signal.HttpStatusCode);
        AddParameter(command, "duration_ms", signal.DurationMs);
        AddJsonParameter(command, "attributes", signal.Attributes.GetRawText());
        AddJsonParameter(command, "body", signal.Body.GetRawText());
        AddParameter(command, "observed_at_utc", signal.ObservedAtUtc);
        AddParameter(command, "received_at_utc", signal.ReceivedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AttachToFaultAsync(Guid signalId, Guid faultId, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE incidentcompass.signals SET fault_id = @fault_id WHERE id = @signal_id;",
            lease.Connection,
            lease.Transaction);

        AddParameter(command, "fault_id", faultId);
        AddParameter(command, "signal_id", signalId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountDistinctNeighborsAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(DISTINCT COALESCE(external_id, trace_id || ':' || span_id, id::text))
            FROM incidentcompass.signals
            WHERE tenant_id = @tenant_id
              AND service_name = @service_name
              AND environment = @environment
              AND fingerprint = @fingerprint
              AND fingerprint_version = @fingerprint_version
              AND observed_at_utc BETWEEN @window_start_utc AND @window_end_utc;
            """, lease.Connection, lease.Transaction);

        AddParameter(command, "tenant_id", tenantId);
        AddParameter(command, "service_name", serviceName);
        AddParameter(command, "environment", environment);
        AddParameter(command, "fingerprint", fingerprint);
        AddParameter(command, "fingerprint_version", fingerprintVersion);
        AddParameter(command, "window_start_utc", windowStartUtc);
        AddParameter(command, "window_end_utc", windowEndUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)(long)result!;
    }

    private static string ToDbString(FingerprintStrength strength) => strength.ToString().ToLowerInvariant();

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static void AddJsonParameter(NpgsqlCommand command, string name, string value)
    {
        command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, value);
    }
}
