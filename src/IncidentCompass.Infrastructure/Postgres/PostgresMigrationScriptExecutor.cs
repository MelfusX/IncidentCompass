using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresMigrationScriptExecutor
{
    public static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scriptName,
        CancellationToken cancellationToken)
    {
        var sql = await ReadAsync(scriptName, cancellationToken);
        await PostgresMigrationSql.ExecuteAsync(connection, transaction, sql, cancellationToken);
    }

    public static async Task<string> ReadAsync(string scriptName, CancellationToken cancellationToken)
    {
        var assembly = typeof(PostgresMigrationRunner).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith(scriptName, StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded PostgreSQL migration script {scriptName} was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}