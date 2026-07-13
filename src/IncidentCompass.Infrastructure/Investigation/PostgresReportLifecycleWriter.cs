using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal static class PostgresReportLifecycleWriter
{
    public static async Task<Guid?> FindLatestReportForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await LockFaultAsync(connection, transaction, faultId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id
            FROM incidentcompass.triage_reports
            WHERE fault_id = @fault_id
            ORDER BY created_at_utc DESC, id DESC
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction);
        command.AddParameter("fault_id", faultId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task LockFaultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id
            FROM incidentcompass.faults
            WHERE id = @fault_id
            FOR UPDATE;
            """, connection, transaction);
        command.AddParameter("fault_id", faultId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new InvalidOperationException($"Fault '{faultId}' was not found while publishing triage report.");
        }
    }
}
