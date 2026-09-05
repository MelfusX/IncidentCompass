using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresFaultTransactionLock
{
    public static async Task LockAsync(
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
            throw new InvalidOperationException($"Fault '{faultId}' was not found while acquiring its transaction lock.");
        }
    }
}
