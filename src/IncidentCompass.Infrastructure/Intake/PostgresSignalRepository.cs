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

        command.AddParameter("id", signal.Id);
        command.AddParameter("tenant_id", signal.TenantId);
        command.AddParameter("source", signal.Source);
        command.AddParameter("fault_id", signal.FaultId);
        command.AddParameter("fingerprint", signal.Fingerprint);
        command.AddParameter("fingerprint_version", signal.FingerprintVersion);
        command.AddParameter("fingerprint_strength", signal.FingerprintStrength.ToLowerDbString());
        command.AddParameter("external_id", signal.ExternalId);
        command.AddParameter("is_suppressed", signal.IsSuppressed);
        command.AddParameter("suppressed_by_fault_id", signal.SuppressedByFaultId);
        command.AddParameter("suppression_reason", signal.SuppressionReason);
        command.AddParameter("trace_id", signal.TraceId);
        command.AddParameter("span_id", signal.SpanId);
        command.AddParameter("parent_span_id", signal.ParentSpanId);
        command.AddParameter("service_name", signal.ServiceName);
        command.AddParameter("environment", signal.Environment);
        command.AddParameter("operation_name", signal.OperationName);
        command.AddParameter("severity", signal.Severity);
        command.AddParameter("error_type", signal.ErrorType);
        command.AddParameter("error_message", signal.ErrorMessage);
        command.AddParameter("summary", signal.Summary);
        command.AddParameter("description", signal.Description);
        command.AddParameter("http_method", signal.HttpMethod);
        command.AddParameter("http_route", signal.HttpRoute);
        command.AddParameter("http_status_code", signal.HttpStatusCode);
        command.AddParameter("duration_ms", signal.DurationMs);
        command.AddJsonbParameter("attributes", signal.Attributes.GetRawText());
        command.AddJsonbParameter("body", signal.Body.GetRawText());
        command.AddParameter("observed_at_utc", signal.ObservedAtUtc);
        command.AddParameter("received_at_utc", signal.ReceivedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AttachToFaultAsync(Guid signalId, Guid faultId, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE incidentcompass.signals SET fault_id = @fault_id WHERE id = @signal_id;",
            lease.Connection,
            lease.Transaction);

        command.AddParameter("fault_id", faultId);
        command.AddParameter("signal_id", signalId);

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

        command.AddParameter("tenant_id", tenantId);
        command.AddParameter("service_name", serviceName);
        command.AddParameter("environment", environment);
        command.AddParameter("fingerprint", fingerprint);
        command.AddParameter("fingerprint_version", fingerprintVersion);
        command.AddParameter("window_start_utc", windowStartUtc);
        command.AddParameter("window_end_utc", windowEndUtc);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)(long)result!;
    }



}
