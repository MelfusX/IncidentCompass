using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionApprovalQueries
{
    public static async Task<IReadOnlyList<ActionApprovalRecord>> ListAsync(
        NpgsqlConnection connection,
        ActionApprovalListFilter filter,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.tenant_id = @tenant_id
              AND (@status::text IS NULL OR a.state = @status)
              AND (@before_created::timestamptz IS NULL OR a.created_at_utc < @before_created
                   OR (a.created_at_utc = @before_created AND a.id < @before_id::uuid))
            ORDER BY a.created_at_utc DESC, a.id DESC
            LIMIT @limit;
            """, connection);
        command.AddParameter("tenant_id", tenantId);
        command.AddParameter("status", filter.Status?.ToStorageValue());
        command.AddParameter("before_created", filter.BeforeCreatedAtUtc);
        command.AddParameter("before_id", filter.BeforeActionId);
        command.AddParameter("limit", filter.Limit);
        var rows = new List<ActionApprovalRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(PostgresActionApprovalReader.Read(reader));
        }

        return rows;
    }

    public static async Task<ActionApprovalRecord?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid actionId,
        string tenantId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var lockClause = forUpdate ? " FOR UPDATE OF a" : string.Empty;
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.id = @action_id AND a.tenant_id = @tenant_id
            """ + lockClause + ";", connection, transaction);
        command.AddParameter("action_id", actionId);
        command.AddParameter("tenant_id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresActionApprovalReader.Read(reader) : null;
    }

    public static async Task<IReadOnlyList<ActionApprovalProvenance>> ReadProvenanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT ordinal, source_type, source_id, artifact_kind, trust_class
            FROM incidentcompass.action_approval_provenance
            WHERE action_id = @action_id
            ORDER BY ordinal;
            """, connection, transaction);
        command.AddParameter("action_id", actionId);
        var rows = new List<ActionApprovalProvenance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ActionApprovalProvenance(
                reader.GetInt32(0), reader.GetString(1), reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ActionApprovalVocabulary.ParseTrust(reader.GetString(4))));
        }

        return rows;
    }

    public static async Task<ActionApprovalRecord?> FindForWorkerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var lockClause = forUpdate ? " FOR UPDATE OF a" : string.Empty;
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.id = @action_id
            """ + lockClause + ";", connection, transaction);
        command.AddParameter("action_id", actionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresActionApprovalReader.Read(reader) : null;
    }

    public static async Task<bool> IsCurrentReportAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord action,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id = @report_id
            FROM incidentcompass.triage_reports
            WHERE fault_id = @fault_id
            ORDER BY created_at_utc DESC, id DESC
            LIMIT 1;
            """, connection, transaction);
        command.AddParameter("report_id", action.OriginReportId);
        command.AddParameter("fault_id", action.FaultId);
        return (bool?)(await command.ExecuteScalarAsync(cancellationToken)) == true;
    }
}
