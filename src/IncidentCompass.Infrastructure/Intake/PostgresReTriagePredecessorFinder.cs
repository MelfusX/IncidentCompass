using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal static class PostgresReTriagePredecessorFinder
{
    public static async Task<(Guid FaultId, Guid ReportId, string Summary, string[] Limitations)?> FindAndLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recurrenceFaultId,
        CancellationToken cancellationToken)
    {
        var faultId = await FindNearestReportedFaultAsync(
            connection, transaction, recurrenceFaultId, cancellationToken);
        if (faultId is null)
        {
            return null;
        }

        await LockFaultAsync(connection, transaction, faultId.Value, cancellationToken);
        return await LoadLatestReportAsync(connection, transaction, faultId.Value, cancellationToken);
    }

    private static async Task<Guid?> FindNearestReportedFaultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recurrenceFaultId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH RECURSIVE ancestors AS (
                SELECT recurrence_of, 1 AS depth
                FROM incidentcompass.faults
                WHERE id = @recurrence_fault_id AND recurrence_of IS NOT NULL
                UNION ALL
                SELECT fault.recurrence_of, ancestors.depth + 1
                FROM incidentcompass.faults fault
                JOIN ancestors ON ancestors.recurrence_of = fault.id
                WHERE fault.recurrence_of IS NOT NULL)
            SELECT ancestors.recurrence_of
            FROM ancestors
            WHERE EXISTS (
                SELECT 1
                FROM incidentcompass.triage_reports report
                WHERE report.fault_id = ancestors.recurrence_of)
            ORDER BY ancestors.depth
            LIMIT 1;
            """, connection, transaction);
        command.AddParameter("recurrence_fault_id", recurrenceFaultId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid faultId ? faultId : null;
    }

    private static async Task LockFaultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM incidentcompass.faults WHERE id = @fault_id FOR UPDATE;", connection, transaction);
        command.AddParameter("fault_id", faultId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new InvalidOperationException($"Re-triage target fault '{faultId}' was not found.");
        }
    }

    private static async Task<(Guid FaultId, Guid ReportId, string Summary, string[] Limitations)?> LoadLatestReportAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.id, r.summary, r.limitations
            FROM incidentcompass.triage_reports r
            WHERE r.fault_id = @fault_id
              AND NOT EXISTS (
                  SELECT 1 FROM incidentcompass.triage_reports successor
                  WHERE successor.supersedes_report_id = r.id)
            ORDER BY r.created_at_utc DESC, r.id DESC
            LIMIT 1;
            """, connection, transaction);
        command.AddParameter("fault_id", faultId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (faultId, reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<string[]>(2))
            : null;
    }
}