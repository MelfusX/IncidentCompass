using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresMigrationLedger(PostgresDataSourceProvider dataSourceProvider)
{
    public Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        dataSourceProvider.OpenConnectionAsync(cancellationToken);

    public async Task EnsureTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS incidentcompass;

            CREATE TABLE IF NOT EXISTS incidentcompass.schema_migrations (
                version integer PRIMARY KEY CHECK (version > 0),
                name text NOT NULL CHECK (length(btrim(name)) > 0),
                checksum text NOT NULL CHECK (length(btrim(checksum)) > 0),
                status text NOT NULL CHECK (status IN ('Applied', 'Failed')),
                applied_at_utc timestamptz NULL,
                failed_at_utc timestamptz NULL,
                error_message text NULL,
                CHECK (
                    (status = 'Applied' AND applied_at_utc IS NOT NULL AND failed_at_utc IS NULL AND error_message IS NULL) OR
                    (status = 'Failed' AND applied_at_utc IS NULL AND failed_at_utc IS NOT NULL AND error_message IS NOT NULL)
                )
            );
            """;
        await PostgresMigrationSql.ExecuteAsync(connection, transaction: null, sql, cancellationToken);
    }

    public async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        PostgresSchemaMigration migration,
        string expectedChecksum,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name, checksum, status
            FROM incidentcompass.schema_migrations
            WHERE version = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(migration.Version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var name = reader.GetString(0);
        var actualChecksum = reader.GetString(1);
        var status = reader.GetString(2);
        if (!string.Equals(name, migration.Name, StringComparison.Ordinal) ||
            !string.Equals(actualChecksum, expectedChecksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration {migration.Version} does not match its durable migration record.");
        }

        return string.Equals(status, "Applied", StringComparison.Ordinal);
    }

    public Task MarkAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgresSchemaMigration migration,
        string checksum,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO incidentcompass.schema_migrations (
                version, name, checksum, status, applied_at_utc)
            VALUES ($1, $2, $3, 'Applied', clock_timestamp())
            ON CONFLICT (version) DO UPDATE
            SET name = EXCLUDED.name,
                checksum = EXCLUDED.checksum,
                status = 'Applied',
                applied_at_utc = EXCLUDED.applied_at_utc,
                failed_at_utc = NULL,
                error_message = NULL;
            """;
        return PostgresMigrationSql.ExecuteAsync(
            connection,
            transaction,
            sql,
            cancellationToken,
            migration.Version,
            migration.Name,
            checksum);
    }

    public async Task RecordFailureAsync(
        PostgresSchemaMigration migration,
        string checksum,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        const string sql = """
            INSERT INTO incidentcompass.schema_migrations (
                version, name, checksum, status, failed_at_utc, error_message)
            VALUES ($1, $2, $3, 'Failed', clock_timestamp(), $4)
            ON CONFLICT (version) DO UPDATE
            SET name = EXCLUDED.name,
                checksum = EXCLUDED.checksum,
                status = 'Failed',
                applied_at_utc = NULL,
                failed_at_utc = EXCLUDED.failed_at_utc,
                error_message = EXCLUDED.error_message;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(migration.Version);
        command.Parameters.AddWithValue(migration.Name);
        command.Parameters.AddWithValue(checksum);
        command.Parameters.AddWithValue(DescribeFailure(exception));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string DescribeFailure(Exception exception)
    {
        var description = exception.GetType().Name + ": " + exception.Message;
        return description.Length <= 1_000 ? description : description[..1_000];
    }
}
