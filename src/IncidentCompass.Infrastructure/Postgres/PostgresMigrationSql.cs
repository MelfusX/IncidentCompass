using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresMigrationSql
{
    public static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue(values[index]);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}