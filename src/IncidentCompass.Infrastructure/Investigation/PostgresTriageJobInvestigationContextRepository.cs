using System.Text.Json;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Intake;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresTriageJobInvestigationContextRepository(PostgresDataSourceProvider dataSourceProvider)
    : ITriageJobInvestigationContextRepository
{
    public async Task<TriageJobInvestigationContext> GetAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        var fault = await LoadFaultAsync(connection, jobId, cancellationToken);
        var signal = await LoadTriggerSignalAsync(connection, fault.TriggerSignalId, cancellationToken);
        var artifacts = await LoadArtifactsAsync(connection, jobId, attempt, cancellationToken);

        return new TriageJobInvestigationContext(fault, signal, artifacts);
    }

    private static async Task<Fault> LoadFaultAsync(
        NpgsqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT f.id, f.trigger_signal_id, f.tenant_id, f.status, f.fingerprint,
                   f.fingerprint_version, f.fingerprint_strength, f.can_group, f.service_name,
                   f.environment, f.severity, f.correlation_id, f.created_at_utc,
                   f.completed_at_utc, f.recurrence_of
            FROM incidentcompass.faults f
            JOIN incidentcompass.triage_jobs j ON j.fault_id = f.id
            WHERE j.id = @job_id;
            """, connection);
        command.AddParameter("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Fault for triage job '{jobId}' was not found.");
        }

        return new Fault(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            Enum.Parse<FaultStatus>(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            Enum.Parse<FingerprintStrength>(reader.GetString(6), ignoreCase: true),
            reader.GetBoolean(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetDateTimeOffset(12),
            reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13),
            reader.IsDBNull(14) ? null : reader.GetGuid(14));
    }

    private static async Task<Signal> LoadTriggerSignalAsync(
        NpgsqlConnection connection,
        Guid signalId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, tenant_id, source, fault_id, fingerprint, fingerprint_version,
                   fingerprint_strength, can_group, external_id, is_suppressed,
                   suppressed_by_fault_id, suppression_reason, trace_id, span_id,
                   parent_span_id, service_name, environment, operation_name, severity,
                   error_type, error_message, summary, description, http_method,
                   http_route, http_status_code, duration_ms, attributes::text, body::text,
                   observed_at_utc, received_at_utc
            FROM incidentcompass.signals
            WHERE id = @signal_id;
            """, connection);
        command.AddParameter("signal_id", signalId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Trigger signal '{signalId}' was not found.");
        }

        using var attributes = JsonDocument.Parse(reader.GetString(27));
        using var body = JsonDocument.Parse(reader.GetString(28));
        return new Signal(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Enum.Parse<FingerprintStrength>(reader.GetString(6), ignoreCase: true),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetString(24),
            reader.IsDBNull(25) ? null : reader.GetInt32(25),
            reader.IsDBNull(26) ? null : reader.GetInt32(26),
            attributes.RootElement.Clone(),
            body.RootElement.Clone(),
            reader.GetDateTimeOffset(29),
            reader.GetDateTimeOffset(30));
    }

    private static async Task<IReadOnlyCollection<TriageArtifact>> LoadArtifactsAsync(
        NpgsqlConnection connection,
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, job_id, attempt, kind, domain_ref, redacted_payload::text, content_hash, created_at_utc
            FROM incidentcompass.triage_artifacts
            WHERE job_id = @job_id
              AND (attempt IS NULL OR attempt = @attempt)
            ORDER BY created_at_utc, id;
            """, connection);
        command.AddParameter("job_id", jobId);
        command.AddParameter("attempt", attempt);

        var artifacts = new List<TriageArtifact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var payload = JsonDocument.Parse(reader.GetString(5));
            artifacts.Add(new TriageArtifact(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Enum.Parse<ArtifactKind>(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                payload.RootElement.Clone(),
                reader.GetString(6),
                reader.GetDateTimeOffset(7)));
        }

        return artifacts;
    }
}
