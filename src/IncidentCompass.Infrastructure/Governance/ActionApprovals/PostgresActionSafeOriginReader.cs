using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionSafeOriginReader
{
    public static async Task<ActionProposalOrigin?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string tenantId,
        Guid reportId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var lockClause = lockRows ? " FOR UPDATE OF r, j" : string.Empty;
        await using var command = new NpgsqlCommand("""
            SELECT r.fault_id, r.job_id, j.attempt, j.config_hash, r.status,
                   j.created_at_utc, j.updated_at_utc
            FROM incidentcompass.triage_reports r
            JOIN incidentcompass.faults f ON f.id = r.fault_id
            JOIN incidentcompass.triage_jobs j ON j.id = r.job_id
            WHERE r.id = @report_id AND f.tenant_id = @tenant_id
              AND j.status = 'Succeeded'
              AND r.status IN ('Completed', 'InsufficientEvidence')
              AND EXISTS (
                  SELECT 1 FROM incidentcompass.triage_ledger l
                  WHERE l.job_id = j.id AND l.attempt = j.attempt
                    AND l.event_type = 'ReportPublished'
                    AND l.payload_ref = 'report:' || r.id::text)
              AND r.id = (
                  SELECT latest.id FROM incidentcompass.triage_reports latest
                  WHERE latest.fault_id = r.fault_id
                  ORDER BY latest.created_at_utc DESC, latest.id DESC LIMIT 1)
            """ + lockClause + ";", connection, transaction);
        command.AddParameter("report_id", reportId);
        command.AddParameter("tenant_id", tenantId);
        Guid faultId;
        Guid jobId;
        int attempt;
        string configHash;
        string reportStatus;
        DateTimeOffset createdAt;
        DateTimeOffset updatedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            faultId = reader.GetGuid(0);
            jobId = reader.GetGuid(1);
            attempt = reader.GetInt32(2);
            configHash = reader.GetString(3);
            reportStatus = reader.GetString(4);
            createdAt = reader.GetDateTimeOffset(5);
            updatedAt = reader.GetDateTimeOffset(6);
        }

        var evidence = await ReadEvidenceAsync(connection, transaction, reportId, cancellationToken);
        var job = new TriageJob(
            jobId, faultId, TriageJobStatus.Succeeded, attempt,
            null, null, null, null, null, configHash, createdAt, updatedAt);
        return new ActionProposalOrigin(
            tenantId, reportId, job,
            string.Equals(reportStatus, "Completed", StringComparison.Ordinal), evidence);
    }

    private static async Task<IReadOnlyList<Guid>> ReadEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT artifact_id
            FROM incidentcompass.triage_evidence
            WHERE report_id = @report_id
            ORDER BY artifact_id;
            """, connection, transaction);
        command.AddParameter("report_id", reportId);
        var evidence = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            evidence.Add(reader.GetGuid(0));
        }

        return evidence;
    }
}
