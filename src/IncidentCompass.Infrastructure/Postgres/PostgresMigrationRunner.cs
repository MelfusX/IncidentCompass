using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresMigrationRunner
{
    private readonly IPostgresMigrationFailureInjector failureInjector;
    private readonly PostgresMigrationLedger migrationLedger;

    public PostgresMigrationRunner(
        PostgresDataSourceProvider dataSourceProvider,
        IPostgresMigrationFailureInjector failureInjector)
    {
        this.failureInjector = failureInjector;
        migrationLedger = new PostgresMigrationLedger(dataSourceProvider);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await migrationLedger.OpenConnectionAsync(cancellationToken);
        await PostgresMigrationLock.AcquireAsync(connection, cancellationToken);
        try
        {
            await migrationLedger.EnsureTableAsync(connection, cancellationToken);

            foreach (var migration in PostgresMigrationCatalog.All)
            {
                var checksum = await migration.ComputeChecksumAsync(cancellationToken);
                if (await migrationLedger.IsAppliedAsync(
                        connection,
                        migration,
                        checksum,
                        cancellationToken))
                {
                    continue;
                }

                try
                {
                    await ApplyMigrationAsync(connection, migration, checksum, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await migrationLedger.RecordFailureAsync(
                        migration,
                        checksum,
                        exception,
                        cancellationToken);
                    throw new InvalidOperationException(
                        $"PostgreSQL migration {migration.Version} ({migration.Name}) failed. " +
                        "See incidentcompass.schema_migrations and host logs for details.",
                        exception);
                }
            }
        }
        finally
        {
            await PostgresMigrationLock.ReleaseAsync(connection);
        }
    }

    private async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        PostgresSchemaMigration migration,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            failureInjector.ThrowIfRequested(migration.Version);
            await migration.ApplyAsync(connection, transaction, cancellationToken);
            await migrationLedger.MarkAppliedAsync(
                connection,
                transaction,
                migration,
                checksum,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}