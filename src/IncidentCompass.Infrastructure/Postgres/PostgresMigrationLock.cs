using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresMigrationLock
{
    private const long Key = 2_593_107_307;

    public static async Task AcquireAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock($1);", connection);
        command.Parameters.AddWithValue(Key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ReleaseAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1);", connection);
        command.Parameters.AddWithValue(Key);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
